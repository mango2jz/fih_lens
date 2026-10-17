using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using FihLens.Application.Dictionary;
using FihLens.Infrastructure.Dictionary;

namespace FihLens.Tests.Infrastructure;

public sealed class DictionaryPackManagerTests
{
    [Fact]
    public async Task EmptyTrustedCatalogStillPersistsDismissalWithoutNetwork()
    {
        string root = NewRoot();
        var handler = new RecordingHandler((_, _) => throw new Xunit.Sdk.XunitException("No network request expected."));
        using var client = new HttpClient(handler);
        using (var manager = new DictionaryPackManager(root, Array.Empty<DictionaryPackDefinition>(), client))
        {
            Assert.Empty(await manager.GetStatusAsync());
            await manager.SetSetupDismissedAsync(true);
            Assert.True(await manager.GetSetupDismissedAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallAsync(DictionaryPackPaths.PackId));
        }

        using var restarted = new DictionaryPackManager(root, new HttpClient(handler));
        Assert.True(await restarted.GetSetupDismissedAsync());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TrustedInstallActivatesAtRestartAndWorksOffline()
    {
        string root = NewRoot();
        var (definition, archiveBytes) = CreateBundle(root);
        var handler = new RecordingHandler((_, _) => Task.FromResult(Response(archiveBytes)));
        using var client = new HttpClient(handler);
        using (var manager = new DictionaryPackManager(root, [definition], client))
        {
            await manager.InstallAsync(definition.PackId);
            var status = Assert.Single(await manager.GetStatusAsync());
            Assert.Equal(DictionaryPackState.InstallPendingRestart, status.State);
            Assert.Equal(1, handler.Calls);
        }

        using var restarted = new DictionaryPackManager(root, [definition]);
        await using var lookup = Assert.IsType<IndexedJapaneseDictionaryLookup>(await restarted.OpenInstalledAsync());
        var result = await lookup.LookupAsync("せい");
        Assert.Equal(["1", "2"], result.Entries.Select(x => x.SourceArticleId).ToArray());
        Assert.Equal(DictionaryPackState.Installed, Assert.Single(await restarted.GetStatusAsync()).State);
    }

    [Fact]
    public async Task InvalidArchiveHashAndUnsafeRedirectDoNotCreatePendingPack()
    {
        string hashRoot = NewRoot();
        var (definition, bytes) = CreateBundle(hashRoot);
        var wrongHashHandler = new RecordingHandler((_, _) => Task.FromResult(Response(bytes.Concat(new byte[] { 0x20 }).ToArray())));
        using (var manager = new DictionaryPackManager(hashRoot, [definition], new HttpClient(wrongHashHandler)))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync(definition.PackId));
            Assert.Equal(DictionaryPackState.NotInstalled, Assert.Single(await manager.GetStatusAsync()).State);
        }
        Assert.Equal(1, wrongHashHandler.Calls);

        string redirectRoot = NewRoot();
        var (redirectDefinition, _) = CreateBundle(redirectRoot);
        var redirectHandler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://attacker.example/pack.zip") }
        }));
        using var redirectManager = new DictionaryPackManager(redirectRoot, [redirectDefinition], new HttpClient(redirectHandler));
        await Assert.ThrowsAsync<InvalidDataException>(() => redirectManager.InstallAsync(redirectDefinition.PackId));
        Assert.Equal(1, redirectHandler.Calls);
        Assert.Equal(DictionaryPackState.NotInstalled, Assert.Single(await redirectManager.GetStatusAsync()).State);
    }

    [Fact]
    public async Task RemovalWaitsForRestartAndPreservesOutsideSentinel()
    {
        string root = NewRoot();
        var (definition, archiveBytes) = CreateBundle(root);
        string sentinel = Path.Combine(root, "packs", "another-pack", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        using var manager = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(archiveBytes)))));
        await manager.InstallAsync(definition.PackId);
        await using var reader = Assert.IsType<IndexedJapaneseDictionaryLookup>(await manager.OpenInstalledAsync());
        await manager.ScheduleRemovalAsync(definition.PackId);
        Assert.Equal(DictionaryPackState.RemovalPendingRestart, Assert.Single(await manager.GetStatusAsync()).State);

        using var restarted = new DictionaryPackManager(root, [definition]);
        Assert.Null(await restarted.OpenInstalledAsync());
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
        Assert.True(Directory.Exists(Path.Combine(root, "packs", definition.PackId, definition.Version)));

        await reader.DisposeAsync();
        Assert.Null(await restarted.OpenInstalledAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "packs", definition.PackId, definition.Version)));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
    }

    [Fact]
    public async Task UnknownRegistryVersionIsNeverPromotedOrDeleted()
    {
        string root = NewRoot();
        var (definition, _) = CreateBundle(root);
        string unknown = Path.Combine(root, "packs", definition.PackId, "unknown-9");
        Directory.CreateDirectory(unknown);
        string sentinel = Path.Combine(unknown, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "unknown");
        await File.WriteAllTextAsync(Path.Combine(root, "installed-packs.json"),
            "{\"ActiveVersion\":\"unknown-9\",\"PendingVersion\":null,\"RemovalPending\":false,\"Generation\":4}");

        using var manager = new DictionaryPackManager(root, [definition]);
        Assert.Null(await manager.OpenInstalledAsync());
        Assert.Equal("unknown", await File.ReadAllTextAsync(sentinel));
        Assert.Equal(DictionaryPackState.Unavailable, Assert.Single(await manager.GetStatusAsync()).State);
    }

    [Fact]
    public async Task VersionRenameBeforeRegistryCommitIsAnUnreferencedRecoverableDirectory()
    {
        string root = NewRoot();
        var (definition, archiveBytes) = CreateBundle(root);
        string versionPath = Path.Combine(root, "packs", definition.PackId, definition.Version);
        using var manager = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(archiveBytes)))),
            stage => { if (stage == "after-version-rename") throw new DictionaryPackFaultPointException(stage); });

        await Assert.ThrowsAsync<DictionaryPackFaultPointException>(() => manager.InstallAsync(definition.PackId));
        Assert.True(Directory.Exists(versionPath));
        using var restarted = new DictionaryPackManager(root, [definition]);
        Assert.Null(await restarted.OpenInstalledAsync());
        Assert.False(Directory.Exists(versionPath));
        Assert.False(File.Exists(Path.Combine(root, "installed-packs.json")));
    }

    [Fact]
    public async Task RegistryCommitSurvivesFailureImmediatelyAfterReplace()
    {
        string root = NewRoot();
        var (definition, archiveBytes) = CreateBundle(root);
        using var manager = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(archiveBytes)))),
            stage => { if (stage == "after-registry-commit") throw new DictionaryPackFaultPointException(stage); });

        await Assert.ThrowsAsync<DictionaryPackFaultPointException>(() => manager.InstallAsync(definition.PackId));
        using var restarted = new DictionaryPackManager(root, [definition]);
        await using var lookup = Assert.IsType<IndexedJapaneseDictionaryLookup>(await restarted.OpenInstalledAsync());
        Assert.Equal(["1", "2"], (await lookup.LookupAsync("せい")).Entries.Select(x => x.SourceArticleId).ToArray());
    }

    private static (DictionaryPackDefinition Definition, byte[] Archive) CreateBundle(string root)
        => CreateBundle(root, "test-1");

    private static (DictionaryPackDefinition Definition, byte[] Archive) CreateBundle(string root, string version)
    {
        string source = Path.Combine(root, "fixture-source");
        var pack = JapanesePackFixture.Create(source);
        if (version != pack.Definition.Version)
        {
            string manifestPath = Path.Combine(source, "manifest.json");
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
            string artifactHash = manifest["artifactSha256"]!.GetValue<string>();
            string packVersion = version.EndsWith($"-s3-{artifactHash[..24]}", StringComparison.OrdinalIgnoreCase)
                ? version : $"{version}-s3-{artifactHash[..24]}";
            manifest["version"] = packVersion;
            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
            File.WriteAllBytes(manifestPath, manifestBytes);
            pack.Definition = pack.Definition with { Version = packVersion, ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes)),
                InstalledBytes = Directory.GetFiles(source).Sum(file => new FileInfo(file).Length) };
        }
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in Directory.GetFiles(source).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.NoCompression);
                using var output = entry.Open();
                using var input = File.OpenRead(file);
                input.CopyTo(output);
            }
        }
        byte[] archive = data.ToArray();
        return (pack.Definition with
        {
            DownloadBytes = archive.Length,
            ArchiveSha256 = Convert.ToHexString(SHA256.HashData(archive)),
            DownloadUri = new Uri("https://example.test/jmdict.zip")
        }, archive);
    }

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Theory]
    [InlineData("../keys.utf8")]
    [InlineData("folder/keys.utf8")]
    [InlineData("KEYS.UTF8")]
    [InlineData("keys.utf8:extra")]
    public async Task UnsafeArchiveNamesAreRejectedEvenWhenArchiveHashIsTrusted(string unsafeName)
    {
        string root = NewRoot();
        var (definition, original) = CreateBundle(root);
        byte[] bytes = RewriteArchive(original, entry => entry.FullName == "keys.utf8" ? unsafeName : entry.FullName);
        definition = definition with { ArchiveSha256 = Convert.ToHexString(SHA256.HashData(bytes)), DownloadBytes = bytes.Length };
        using var manager = new DictionaryPackManager(root, [definition], new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(bytes)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync(definition.PackId));
        Assert.False(File.Exists(Path.Combine(root, "installed-packs.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "packs", definition.PackId, definition.Version)));
    }

    [Fact]
    public async Task CorruptPendingUpdatePreservesAuthenticatedActiveVersion()
    {
        string root = NewRoot();
        var (first, firstBytes) = CreateBundle(root);
        using (var manager = new DictionaryPackManager(root, [first], new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(firstBytes))))))
            await manager.InstallAsync(first.PackId);
        using (var firstStartup = new DictionaryPackManager(root, [first]))
        { await using var active = Assert.IsType<IndexedJapaneseDictionaryLookup>(await firstStartup.OpenInstalledAsync()); }
        var (next, nextBytes) = CreateBundle(root, "test-2");
        using (var update = new DictionaryPackManager(root, [first, next], new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(nextBytes))))))
            await update.InstallAsync(next.PackId);
        await File.AppendAllTextAsync(Path.Combine(root, "packs", next.PackId, next.Version, "entries.jsonl"), "tampered");
        using var restarted = new DictionaryPackManager(root, [first, next]);
        await using var retained = Assert.IsType<IndexedJapaneseDictionaryLookup>(await restarted.OpenInstalledAsync());
        Assert.Equal(["1", "2"], (await retained.LookupAsync("せい")).Entries.Select(entry => entry.SourceArticleId).ToArray());
        var statuses = await restarted.GetStatusAsync();
        Assert.Equal(DictionaryPackState.Installed, statuses.Single(status => status.Definition.Version == first.Version).State);
        var failed = statuses.Single(status => status.Definition.Version == next.Version);
        Assert.Equal(DictionaryPackState.Failed, failed.State);
        Assert.Contains("retry", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(statuses, status => status.State is DictionaryPackState.InstallPendingRestart or DictionaryPackState.UpdatePendingRestart);
        using var retry = new DictionaryPackManager(root, [first, next], new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(nextBytes)))));
        await retry.InstallAsync(next.PackId);
        Assert.Equal(DictionaryPackState.UpdatePendingRestart, (await retry.GetStatusAsync()).Single(status => status.Definition.Version == next.Version).State);
        Assert.True((await retained.LookupAsync("せい")).IsMatch);
    }

    [Fact]
    public async Task FailedInitialInstallCanBeRemovedAndClearedAtRestart()
    {
        string root = NewRoot();
        var (definition, bytes) = CreateBundle(root);
        using (var install = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(bytes))))))
            await install.InstallAsync(definition.PackId);
        await File.AppendAllTextAsync(Path.Combine(root, "packs", definition.PackId, definition.Version, "entries.jsonl"), "tampered");
        using (var failed = new DictionaryPackManager(root, [definition]))
        {
            Assert.Null(await failed.OpenInstalledAsync());
            Assert.Equal(DictionaryPackState.Failed, Assert.Single(await failed.GetStatusAsync()).State);
            await failed.ScheduleRemovalAsync(definition.PackId);
            Assert.Equal(DictionaryPackState.RemovalPendingRestart, Assert.Single(await failed.GetStatusAsync()).State);
        }
        using var restarted = new DictionaryPackManager(root, [definition]);
        Assert.Null(await restarted.OpenInstalledAsync());
        Assert.Equal(DictionaryPackState.NotInstalled, Assert.Single(await restarted.GetStatusAsync()).State);
        Assert.False(Directory.Exists(Path.Combine(root, "packs", definition.PackId, definition.Version)));
    }

    [Theory]
    [InlineData("{invalid registry bytes")]
    [InlineData("oversized")]
    [InlineData("{\"FailedVersion\":\"../escape\",\"FailureMessage\":\"untrusted message\"}")]
    [InlineData("{\"FailedVersion\":\"test-1\",\"FailureMessage\":\"untrusted message\"}")]
    public async Task ExplicitInstallRepairsCorruptRegistryWithoutInferringFolders(string corrupt)
    {
        if (corrupt == "oversized") corrupt = new string('x', 16 * 1024 + 1);
        string root = NewRoot();
        var (definition, bytes) = CreateBundle(root);
        Directory.CreateDirectory(Path.Combine(root, "packs", definition.PackId, "unknown-keep"));
        string unknown = Path.Combine(root, "packs", definition.PackId, "unknown-keep", "sentinel");
        await File.WriteAllTextAsync(unknown, "keep");
        await File.WriteAllTextAsync(Path.Combine(root, "installed-packs.json"), corrupt);
        using var manager = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(bytes)))));

        await manager.InstallAsync(definition.PackId);
        Assert.Equal(DictionaryPackState.InstallPendingRestart, Assert.Single(await manager.GetStatusAsync()).State);
        string quarantined = Assert.Single(Directory.GetFiles(root, "installed-packs.corrupt-*.json"));
        Assert.Equal(corrupt, await File.ReadAllTextAsync(quarantined));
        Assert.Equal("keep", await File.ReadAllTextAsync(unknown));
        using var restarted = new DictionaryPackManager(root, [definition]);
        await using var reader = Assert.IsType<IndexedJapaneseDictionaryLookup>(await restarted.OpenInstalledAsync());
        Assert.True((await reader.LookupAsync("せい")).IsMatch);
    }

    [Fact]
    public async Task StartupCleansOnlyOwnedStagingAndSupersededCatalogVersions()
    {
        string root = NewRoot();
        var (one, oneBytes) = CreateBundle(root, "test-1");
        using (var install = new DictionaryPackManager(root, [one],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(oneBytes))))))
            await install.InstallAsync(one.PackId);
        using (var firstStartup = new DictionaryPackManager(root, [one]))
        { await using var active = Assert.IsType<IndexedJapaneseDictionaryLookup>(await firstStartup.OpenInstalledAsync()); }
        var (two, twoBytes) = CreateBundle(root, "test-2");
        var (three, threeBytes) = CreateBundle(root, "test-3");
        using (var update = new DictionaryPackManager(root, [one, two],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(twoBytes))))))
            await update.InstallAsync(two.PackId);
        string stagingRoot = Path.Combine(root, ".staging");
        string abandoned = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, ".fihlens-owned"), "FihLens-JMdict-staging-v1");
        await File.WriteAllTextAsync(Path.Combine(abandoned, "pack.zip"), "partial");
        string unowned = Path.Combine(stagingRoot, "user-data");
        Directory.CreateDirectory(unowned);
        await File.WriteAllTextAsync(Path.Combine(unowned, "keep.txt"), "keep");
        using (var startup = new DictionaryPackManager(root, [one, two]))
        { await using var active = Assert.IsType<IndexedJapaneseDictionaryLookup>(await startup.OpenInstalledAsync()); }
        using (var update = new DictionaryPackManager(root, [one, two, three],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(threeBytes))))))
            await update.InstallAsync(three.PackId);
        using (var startup = new DictionaryPackManager(root, [one, two, three]))
        { await using var active = Assert.IsType<IndexedJapaneseDictionaryLookup>(await startup.OpenInstalledAsync()); }

        Assert.False(Directory.Exists(abandoned));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(unowned, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, "packs", one.PackId, one.Version)));
        Assert.True(Directory.Exists(Path.Combine(root, "packs", one.PackId, two.Version)));
        Assert.True(Directory.Exists(Path.Combine(root, "packs", one.PackId, three.Version)));
    }

    [Fact]
    public async Task ConcurrentStartupValidatesOutsideCrossProcessMutationLock()
    {
        string root = NewRoot();
        var (definition, bytes) = CreateBundle(root);
        using (var install = new DictionaryPackManager(root, [definition],
            new HttpClient(new RecordingHandler((_, _) => Task.FromResult(Response(bytes))))))
            await install.InstallAsync(definition.PackId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new DictionaryPackManager(root, [definition], faultPoint: stage =>
        {
            if (stage == "startup-validation-start")
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }
        });
        using var second = new DictionaryPackManager(root, [definition]);
        Task<IndexedJapaneseDictionaryLookup?> firstOpen = first.OpenInstalledAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var secondReader = Assert.IsType<IndexedJapaneseDictionaryLookup>(
                await second.OpenInstalledAsync().WaitAsync(TimeSpan.FromSeconds(4)));
        }
        finally { release.TrySetResult(); }
        await using var firstReader = Assert.IsType<IndexedJapaneseDictionaryLookup>(await firstOpen.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True((await firstReader.LookupAsync("せい")).IsMatch);
    }

    [Fact]
    public async Task CancelledDownloadNeverPublishesPendingAndKeepsSetupUsable()
    {
        string root = NewRoot();
        var (definition, _) = CreateBundle(root);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var manager = new DictionaryPackManager(root, [definition], new HttpClient(new RecordingHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var install = manager.InstallAsync(definition.PackId, cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        Assert.False(File.Exists(Path.Combine(root, "installed-packs.json")));
        await manager.SetSetupDismissedAsync(true);
        Assert.True(await manager.GetSetupDismissedAsync());
    }

    private static byte[] RewriteArchive(byte[] original, Func<ZipArchiveEntry, string> name)
    {
        using var input = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read);
        using var buffer = new MemoryStream();
        using (var output = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in input.Entries)
            {
                using var source = entry.Open();
                using var target = output.CreateEntry(name(entry), CompressionLevel.NoCompression).Open();
                source.CopyTo(target);
            }
        return buffer.ToArray();
    }
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "FihLens-pack-test-" + Guid.NewGuid().ToString("N"));

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }
}
