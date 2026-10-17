using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FihLens.Application.Dictionary;

namespace FihLens.Infrastructure.Dictionary;

public sealed class DictionaryPackManager : IDictionaryPackManager, IDisposable
{
    private const string StagingOwnerFile = ".fihlens-owned";
    private const string StagingOwnerValue = "FihLens-JMdict-staging-v1";
    private const string PendingFailureMessage = "The pending dictionary update failed verification; retry the update.";
    private static readonly string[] Members =
    ["entries.jsonl", "keys.utf8", "lookup.idx", "manifest.json", "LICENSE-DATA.txt", "ATTRIBUTION.txt", "CHANGES.txt"];
    private readonly DictionaryPackPaths _paths;
    private readonly DictionaryPackCatalog _catalog;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Action<string>? _faultPoint;
    private readonly SemaphoreSlim _localMutation = new(1, 1);
    private string? _validatedActiveVersion;

    public DictionaryPackManager(string? localAssetRoot = null, HttpClient? httpClient = null)
        : this(new DictionaryPackPaths(localAssetRoot), DictionaryPackCatalog.BuiltIn, httpClient) { }

    internal DictionaryPackManager(string localAssetRoot, IEnumerable<DictionaryPackDefinition> catalog, HttpClient? httpClient = null,
        Action<string>? faultPoint = null)
        : this(new DictionaryPackPaths(localAssetRoot), new DictionaryPackCatalog(catalog), httpClient, faultPoint) { }

    private DictionaryPackManager(DictionaryPackPaths paths, DictionaryPackCatalog catalog, HttpClient? httpClient,
        Action<string>? faultPoint = null)
    {
        _paths = paths;
        _catalog = catalog;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = httpClient is null;
        _faultPoint = faultPoint;
    }

    public async Task<IReadOnlyList<DictionaryPackStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var mutation = await AcquireMutationAsync(cancellationToken).ConfigureAwait(false);
        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var statuses = new List<DictionaryPackStatus>();
        foreach (var definition in _catalog.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DictionaryPackState state = DictionaryPackState.NotInstalled;
            string? message = null;
            if (registry is null)
            {
                state = File.Exists(_paths.RegistryPath) ? DictionaryPackState.Failed : DictionaryPackState.NotInstalled;
                if (state == DictionaryPackState.Failed) message = "Pack status is unavailable; reinstall from Dictionaries.";
            }
            else if (registry.RemovalPending)
                state = DictionaryPackState.RemovalPendingRestart;
            else if (registry.PendingVersion == definition.Version)
                state = registry.ActiveVersion is null ? DictionaryPackState.InstallPendingRestart : DictionaryPackState.UpdatePendingRestart;
            else if (registry.FailedVersion == definition.Version)
            {
                state = DictionaryPackState.Failed;
                message = registry.FailureMessage ?? "The pending dictionary update failed verification; retry the update.";
            }
            else if (registry.ActiveVersion == definition.Version)
            {
                state = _validatedActiveVersion == definition.Version ? DictionaryPackState.Installed : DictionaryPackState.Failed;
                if (state == DictionaryPackState.Failed) message = "Installed dictionary data failed verification.";
            }
            else if (registry.ActiveVersion is not null &&
                !_catalog.TryGet(DictionaryPackPaths.PackId, registry.ActiveVersion, out _))
            {
                state = DictionaryPackState.Unavailable;
                message = "Installed dictionary version is unsupported; reinstall an available version.";
            }
            else if (registry.ActiveVersion is null && registry.PendingVersion is not null &&
                !_catalog.TryGet(DictionaryPackPaths.PackId, registry.PendingVersion, out _))
            {
                state = DictionaryPackState.Unavailable;
                message = "Pending dictionary version is unsupported; reinstall an available version.";
            }
            statuses.Add(new DictionaryPackStatus(definition, state, message));
        }
        return statuses;
    }

    public async Task InstallAsync(string packId, IProgress<DictionaryPackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_catalog.TryGetLatest(packId, out var definition))
            throw new InvalidOperationException("No trusted downloadable version is available for this dictionary.");
        await _localMutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var mutation = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
            bool corruptRegistry = registry is null && File.Exists(_paths.RegistryPath);
            registry ??= new(null, null, false, 0);
            if (registry.RemovalPending) throw new InvalidOperationException("Restart the app before installing this dictionary again.");
            if (registry.PendingVersion == definition.Version) return;
            if (registry.ActiveVersion == definition.Version)
            {
                if (_validatedActiveVersion == definition.Version) return;
                throw new InvalidOperationException("Remove this failed installation and restart before reinstalling it.");
            }

            EnsureDiskSpace(registry, definition);
            string operation = Guid.NewGuid().ToString("N");
            string staging = _paths.StagingDirectory(operation);
            Directory.CreateDirectory(staging);
            DictionaryPackPaths.RejectReparseAncestors(staging);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(staging, StagingOwnerFile), StagingOwnerValue, cancellationToken).ConfigureAwait(false);
                string archivePath = Path.Combine(staging, "pack.zip");
                progress?.Report(new("Downloading", 0, definition.DownloadBytes));
                await DownloadAsync(definition, archivePath, progress, cancellationToken).ConfigureAwait(false);
                DictionaryPackPaths.RejectReparseAncestors(staging);
                ExtractArchive(archivePath, staging, definition, cancellationToken, progress);
                File.Delete(archivePath);

                progress?.Report(new("Verifying", 0, null));
                try
                {
                    await using var reader = await IndexedJapaneseDictionaryLookup.LoadAsync(staging, definition, cancellationToken).ConfigureAwait(false);
                }
                catch (DictionaryDataException)
                {
                    throw new InvalidDataException("Dictionary pack content failed verification.");
                }
                cancellationToken.ThrowIfCancellationRequested();

                string destination = _paths.VersionDirectory(definition.Version);
                if (Directory.Exists(destination))
                {
                    bool complete;
                    try
                    {
                        await using var existing = await IndexedJapaneseDictionaryLookup.LoadAsync(destination, definition, cancellationToken).ConfigureAwait(false);
                        complete = true;
                    }
                    catch (DictionaryDataException) { complete = false; }
                    if (!complete && (corruptRegistry || registry.RollbackVersion == definition.Version || !TryDeleteVersion(definition.Version)))
                        throw new IOException("Dictionary version is busy or contains files that cannot be replaced safely.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                DictionaryPackPaths.RejectReparseAncestors(staging);
                DictionaryPackPaths.RejectReparseAncestors(destination);
                ReachFaultPoint("before-version-rename");
                if (!Directory.Exists(destination)) Directory.Move(staging, destination);
                string ownerMarker = Path.Combine(destination, StagingOwnerFile);
                if (File.Exists(ownerMarker)) File.Delete(ownerMarker);
                ReachFaultPoint("after-version-rename");
                cancellationToken.ThrowIfCancellationRequested();
                ReachFaultPoint("before-registry-commit");
                if (corruptRegistry) QuarantineCorruptRegistry();
                await WriteRegistryAsync(registry with
                {
                    PendingVersion = definition.Version,
                    RemovalPending = false,
                    FailedVersion = null,
                    FailureMessage = null,
                    Generation = checked(registry.Generation + 1)
                }, cancellationToken).ConfigureAwait(false);
                ReachFaultPoint("after-registry-commit");
                progress?.Report(new("Installed — restart to enable", definition.DownloadBytes, definition.DownloadBytes));
            }
            finally
            {
                TryDeleteOwnedStaging(staging);
            }
        }
        finally { _localMutation.Release(); }
    }

    public async Task ScheduleRemovalAsync(string packId, CancellationToken cancellationToken = default)
    {
        if (packId != DictionaryPackPaths.PackId || !_catalog.Definitions.Any(x => x.PackId == packId))
            throw new InvalidOperationException("This dictionary cannot be removed because no trusted pack is installed.");
        await _localMutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var mutation = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
            if (registry is null && File.Exists(_paths.RegistryPath))
                throw new InvalidOperationException("Dictionary status is corrupt; no changes were made.");
            if (registry is null || (registry.ActiveVersion is null && registry.PendingVersion is null && registry.FailedVersion is null)) return;
            ReachFaultPoint("before-removal-commit");
            await WriteRegistryAsync(registry with
            {
                PendingVersion = null,
                RemovalPending = true,
                Generation = checked(registry.Generation + 1)
            }, cancellationToken).ConfigureAwait(false);
            ReachFaultPoint("after-removal-commit");
        }
        finally { _localMutation.Release(); }
    }

    public async Task<bool> GetSetupDismissedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            DictionaryPackPaths.RejectReparseAncestors(_paths.SetupPreferencePath);
            if (!File.Exists(_paths.SetupPreferencePath)) return false;
            await using var stream = new FileStream(_paths.SetupPreferencePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > 1024) return false;
            var preference = await JsonSerializer.DeserializeAsync<SetupPreference>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return preference?.Dismissed == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return false; }
    }

    public async Task SetSetupDismissedAsync(bool dismissed, CancellationToken cancellationToken = default)
    {
        await _localMutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var mutation = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(_paths.Root);
            await DictionaryPackAtomicFile.WriteAsync(_paths.SetupPreferencePath, new SetupPreference(dismissed), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { _localMutation.Release(); }
    }

    public async Task<IndexedJapaneseDictionaryLookup?> OpenInstalledAsync(CancellationToken cancellationToken = default)
    {
        await _localMutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool forceRollback = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                DictionaryPackRegistry? registry;
                DictionaryPackDefinition? selected = null;
                bool isPending = false;
                IndexedJapaneseDictionaryLookup.ValidationCandidate? candidate = null;
                bool candidateOpenFailed = false;
                using (var mutation = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false))
                {
                    CleanupOwnedStaging();
                    registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
                    if (registry is null)
                    {
                        if (!File.Exists(_paths.RegistryPath))
                        {
                            DeleteKnownVersions();
                        }
                        return null;
                    }
                    if (registry.RemovalPending)
                    {
                        await WriteRegistryAsync(registry with
                        {
                            ActiveVersion = null, PendingVersion = null, RollbackVersion = null,
                            FailedVersion = null, FailureMessage = null, RemovalPending = false,
                            Generation = checked(registry.Generation + 1)
                        }, cancellationToken).ConfigureAwait(false);
                        _validatedActiveVersion = null;
                        DeleteKnownVersions();
                        return null;
                    }

                    string? version = null;
                    if (!forceRollback && registry.PendingVersion is { } pendingVersion &&
                        _catalog.TryGet(DictionaryPackPaths.PackId, pendingVersion, out var pending))
                    {
                        selected = pending;
                        isPending = true;
                        version = pending.Version;
                    }
                    else if (!forceRollback && registry.ActiveVersion is { } activeVersion &&
                        _catalog.TryGet(DictionaryPackPaths.PackId, activeVersion, out var active))
                    {
                        selected = active;
                        version = active.Version;
                    }
                    else if (registry.RollbackVersion is { } rollbackVersion &&
                        _catalog.TryGet(DictionaryPackPaths.PackId, rollbackVersion, out var rollback))
                    {
                        selected = rollback;
                        version = rollback.Version;
                        forceRollback = true;
                    }
                    if (selected is null)
                    {
                        CleanupSupersededVersions(registry);
                        return null;
                    }
                    try
                    {
                        candidate = await IndexedJapaneseDictionaryLookup.OpenForValidationAsync(
                            _paths.VersionDirectory(selected.Version), selected, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DictionaryDataException)
                    { candidateOpenFailed = true; }
                }

                IndexedJapaneseDictionaryLookup? reader = null;
                if (!candidateOpenFailed)
                {
                    using (candidate)
                    {
                        ReachFaultPoint("startup-validation-start");
                        try { reader = await candidate!.ValidateAsync(cancellationToken).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or DictionaryDataException)
                        { candidateOpenFailed = true; }
                    }
                }

                bool readerTransferred = false;
                try
                {
                    using (var mutation = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var latest = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
                        if (latest is null || latest.Generation != registry!.Generation)
                        {
                            forceRollback = false;
                            continue;
                        }

                        if (isPending)
                        {
                            if (candidateOpenFailed)
                            {
                                await WriteRegistryAsync(latest with
                                {
                                    PendingVersion = null, FailedVersion = selected.Version,
                                    FailureMessage = PendingFailureMessage,
                                    Generation = checked(latest.Generation + 1)
                                }, cancellationToken).ConfigureAwait(false);
                                forceRollback = false;
                                continue;
                            }
                            ReachFaultPoint("before-activation-commit");
                            var activated = latest with
                            {
                                ActiveVersion = selected.Version, PendingVersion = null,
                                RollbackVersion = IsTrustedVersion(latest.ActiveVersion) ? latest.ActiveVersion : null,
                                FailedVersion = null, FailureMessage = null,
                                Generation = checked(latest.Generation + 1)
                            };
                            await WriteRegistryAsync(activated, cancellationToken).ConfigureAwait(false);
                            ReachFaultPoint("after-activation-commit");
                            _validatedActiveVersion = selected.Version;
                            CleanupSupersededVersions(activated);
                            readerTransferred = true;
                            return reader;
                        }

                        if (candidateOpenFailed)
                        {
                            if (!forceRollback && latest.RollbackVersion is not null)
                            {
                                forceRollback = true;
                                continue;
                            }
                            _validatedActiveVersion = null;
                            return null;
                        }
                        if (forceRollback && latest.RollbackVersion == selected.Version)
                        {
                            var restored = latest with
                            {
                                ActiveVersion = selected.Version, RollbackVersion = null,
                                Generation = checked(latest.Generation + 1)
                            };
                            await WriteRegistryAsync(restored, cancellationToken).ConfigureAwait(false);
                            _validatedActiveVersion = selected.Version;
                            CleanupSupersededVersions(restored);
                        }
                        else
                        {
                            _validatedActiveVersion = selected.Version;
                            CleanupSupersededVersions(latest);
                        }
                        readerTransferred = true;
                        return reader;
                    }
                }
                finally
                {
                    if (!readerTransferred && reader is not null)
                        await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
            return null;
        }
        finally { _localMutation.Release(); }
    }

    private async Task DownloadAsync(DictionaryPackDefinition definition, string path,
        IProgress<DictionaryPackProgress>? progress, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        token = deadline.Token;
        var origin = definition.DownloadUri!;
        Uri current = origin;
        for (int redirects = 0; ; redirects++)
        {
            if (!AllowedDownloadOrigin(origin, current))
                throw new InvalidDataException("Dictionary download redirect was rejected.");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } finalUri &&
                !AllowedDownloadOrigin(origin, finalUri))
                throw new InvalidDataException("Dictionary download redirect was rejected.");
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirects >= 5 || response.Headers.Location is not { } location)
                    throw new InvalidDataException("Dictionary download redirect limit reached.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long declared && declared != definition.DownloadBytes)
                throw new InvalidDataException("Dictionary download size did not match the trusted catalog.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            DictionaryPackPaths.RejectReparseAncestors(path);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                total = checked(total + count);
                if (total > definition.DownloadBytes || total > DictionaryPackCatalog.MaximumArchiveBytes)
                    throw new InvalidDataException("Dictionary archive exceeded its trusted size limit.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                progress?.Report(new("Downloading", total, definition.DownloadBytes));
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            if (total != definition.DownloadBytes || !HashEquals(Convert.ToHexString(hash.GetHashAndReset()), definition.ArchiveSha256))
                throw new InvalidDataException("Dictionary archive did not match the trusted catalog.");
            return;
        }
    }

    private static void ExtractArchive(string archivePath, string destination, DictionaryPackDefinition definition,
        CancellationToken token, IProgress<DictionaryPackProgress>? progress)
    {
        using var archiveFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read);
        if (archive.Entries.Count != Members.Length) throw new InvalidDataException("Dictionary archive member list was invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName;
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains('\0') ||
                !Members.Contains(name, StringComparer.Ordinal) || !names.Add(name) || (unixType != 0 && unixType != 0x8000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                entry.Length < 0 || entry.CompressedLength < 0 || entry.Length > DictionaryPackCatalog.MaximumExtractedBytes ||
                (entry.Length > 0 && entry.CompressedLength < (entry.Length + 99) / 100))
                throw new InvalidDataException("Dictionary archive contained an unsafe member.");
            total = checked(total + entry.Length);
            if (total > DictionaryPackCatalog.MaximumExtractedBytes) throw new InvalidDataException("Dictionary archive exceeded its extraction limit.");
            if (name is "manifest.json" or "LICENSE-DATA.txt" or "ATTRIBUTION.txt" or "CHANGES.txt" && entry.Length > 1024 * 1024)
                throw new InvalidDataException("Dictionary notice exceeded its size limit.");
            string path = Path.Combine(destination, name);
            DictionaryPackPaths.RejectReparseAncestors(path);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
            byte[] buffer = new byte[64 * 1024];
            long written = 0;
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                token.ThrowIfCancellationRequested();
                written = checked(written + count);
                if (written > entry.Length || written > DictionaryPackCatalog.MaximumExtractedBytes)
                    throw new InvalidDataException("Dictionary archive member exceeded its declared size.");
                output.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("Dictionary archive member was truncated.");
            progress?.Report(new("Extracting", total, definition.InstalledBytes));
        }
        if (total != definition.InstalledBytes || names.Count != Members.Length)
            throw new InvalidDataException("Dictionary archive did not match the trusted member list or size.");
        string manifestPath = Path.Combine(destination, "manifest.json");
        DictionaryPackPaths.RejectReparseAncestors(manifestPath);
        if (!HashEquals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))), definition.ManifestSha256))
            throw new InvalidDataException("Dictionary manifest did not match the trusted catalog.");
    }

    private async Task<DictionaryPackRegistry?> ReadRegistryAsync(CancellationToken token)
    {
        string path = _paths.RegistryPath;
        DictionaryPackPaths.RejectReparseAncestors(path);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > 16 * 1024) return null;
            var value = await JsonSerializer.DeserializeAsync<DictionaryPackRegistry>(stream, cancellationToken: token).ConfigureAwait(false);
            if (value is null || value.Generation < 0 ||
                (value.ActiveVersion is not null && !DictionaryPackPaths.IsSafeId(value.ActiveVersion)) ||
            (value.PendingVersion is not null && !DictionaryPackPaths.IsSafeId(value.PendingVersion)) ||
            (value.RollbackVersion is not null && !DictionaryPackPaths.IsSafeId(value.RollbackVersion)) ||
            (value.FailedVersion is not null && !DictionaryPackPaths.IsSafeId(value.FailedVersion)) ||
            (value.FailedVersion is null) != (value.FailureMessage is null) ||
            (value.FailureMessage is not null && value.FailureMessage != PendingFailureMessage)) return null;
            return value;
        }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private async Task WriteRegistryAsync(DictionaryPackRegistry registry, CancellationToken token)
    {
        Directory.CreateDirectory(_paths.Root);
        await DictionaryPackAtomicFile.WriteAsync(_paths.RegistryPath, registry, token).ConfigureAwait(false);
    }

    private void EnsureDiskSpace(DictionaryPackRegistry registry, DictionaryPackDefinition definition)
    {
        Directory.CreateDirectory(_paths.Root);
        var drive = new DriveInfo(Path.GetPathRoot(_paths.Root)!);
        long retained = 0;
        var retainedVersions = new HashSet<string>(StringComparer.Ordinal);
        if (registry.ActiveVersion is { } activeVersion && retainedVersions.Add(activeVersion) &&
            _catalog.TryGet(DictionaryPackPaths.PackId, activeVersion, out var active)) retained = active.InstalledBytes;
        if (registry.RollbackVersion is { } rollbackVersion && retainedVersions.Add(rollbackVersion) &&
            _catalog.TryGet(DictionaryPackPaths.PackId, rollbackVersion, out var rollback))
            retained = checked(retained + rollback.InstalledBytes);
        long needed = checked(definition.DownloadBytes + definition.InstalledBytes + retained +
            (definition.DownloadBytes + definition.InstalledBytes + retained + 9) / 10);
        if (drive.AvailableFreeSpace < needed) throw new IOException("Not enough free space to install this dictionary safely.");
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_paths.Root);
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            DictionaryPackPaths.RejectReparseAncestors(_paths.LockPath);
            try { return new FileStream(_paths.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until)
            { await Task.Delay(50, token).ConfigureAwait(false); }
            catch (IOException) { throw new IOException("Dictionary files are busy in another app instance. Try again shortly."); }
        }
    }

    private async Task<MutationLease> AcquireMutationAsync(CancellationToken token)
    {
        await _localMutation.WaitAsync(token).ConfigureAwait(false);
        try { return new MutationLease(_localMutation, await AcquireFileLockAsync(token).ConfigureAwait(false)); }
        catch { _localMutation.Release(); throw; }
    }

    private void DeleteKnownVersions()
    {
        foreach (var definition in _catalog.Definitions)
            TryDeleteVersion(definition.Version);
    }

    private void CleanupSupersededVersions(DictionaryPackRegistry registry)
    {
        var retained = new HashSet<string>(StringComparer.Ordinal);
        if (registry.ActiveVersion is not null) retained.Add(registry.ActiveVersion);
        if (registry.PendingVersion is not null) retained.Add(registry.PendingVersion);
        if (registry.RollbackVersion is not null) retained.Add(registry.RollbackVersion);
        if (registry.FailedVersion is not null) retained.Add(registry.FailedVersion);
        foreach (var definition in _catalog.Definitions)
            if (!retained.Contains(definition.Version)) TryDeleteVersion(definition.Version);
    }

    private void CleanupOwnedStaging()
    {
        foreach (string path in _paths.EnumerateStagingDirectories())
        {
            string id = Path.GetFileName(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                DictionaryPackPaths.RejectReparseAncestors(path);
                if (!HasStagingOwnership(path)) continue;
                TryDeleteOwnedStaging(path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void QuarantineCorruptRegistry()
    {
        DictionaryPackPaths.RejectReparseAncestors(_paths.RegistryPath);
        if (!File.Exists(_paths.RegistryPath)) return;
        string quarantine = _paths.Child("installed-packs.corrupt-" + Guid.NewGuid().ToString("N") + ".json");
        File.Move(_paths.RegistryPath, quarantine);
    }

    private bool TryDeleteVersion(string version)
    {
        string path = _paths.VersionDirectory(version);
        if (!Directory.Exists(path)) return true;
        try
        {
            string marker = Path.Combine(path, StagingOwnerFile);
            if (File.Exists(marker))
            {
                if (!HasStagingOwnership(path)) return false;
                File.Delete(marker);
            }
            foreach (string name in Members) DeleteOwnedFile(Path.Combine(path, name));
            Directory.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool HasStagingOwnership(string directory)
    {
        string marker = Path.Combine(directory, StagingOwnerFile);
        DictionaryPackPaths.RejectReparseAncestors(marker);
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != StagingOwnerValue.Length) return false;
        Span<byte> bytes = stackalloc byte[StagingOwnerValue.Length];
        stream.ReadExactly(bytes);
        return System.Text.Encoding.UTF8.GetString(bytes) == StagingOwnerValue;
    }

    private static void TryDeleteOwnedStaging(string path)
    {
        try
        {
            DictionaryPackPaths.RejectReparseAncestors(path);
            if (!Directory.Exists(path)) return;
            foreach (string name in Members.Append("pack.zip").Append(StagingOwnerFile)) DeleteOwnedFile(Path.Combine(path, name));
            Directory.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteOwnedFile(string path)
    {
        DictionaryPackPaths.RejectReparseAncestors(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;
    private static bool AllowedDownloadOrigin(Uri origin, Uri candidate) =>
        candidate.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(candidate.UserInfo) &&
        (SameOrigin(origin, candidate) || origin.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && origin.Port == 443 &&
            origin.AbsolutePath.StartsWith("/mango2jz/fih_lens/releases/download/", StringComparison.Ordinal) &&
            candidate.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) && candidate.Port == 443);
    private static bool HashEquals(string actual, string expected) => StringComparer.OrdinalIgnoreCase.Equals(actual, expected);
    private bool IsTrustedVersion(string? version) => version is not null &&
        _catalog.TryGet(DictionaryPackPaths.PackId, version, out _);
    private void ReachFaultPoint(string stage) => _faultPoint?.Invoke(stage);
    private sealed record SetupPreference(bool Dismissed);
    private sealed class MutationLease(SemaphoreSlim semaphore, FileStream stream) : IDisposable
    {
        private FileStream? _stream = stream;
        public void Dispose() { _stream?.Dispose(); _stream = null; semaphore.Release(); }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _localMutation.Dispose();
    }
}

internal sealed class DictionaryPackFaultPointException(string stage) : Exception($"Injected lifecycle failure at {stage}.");
