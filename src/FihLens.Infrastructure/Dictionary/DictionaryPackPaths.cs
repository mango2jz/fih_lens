using System.Text.Json;

namespace FihLens.Infrastructure.Dictionary;

internal sealed class DictionaryPackPaths
{
    public const string PackId = "jmdict-ja-en";
    private readonly string _root;

    public DictionaryPackPaths(string? root = null)
    {
        string configured = root ?? Environment.GetEnvironmentVariable("FIHLENS_LOCAL_ASSET_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FihLens", "local-assets");
        if (string.IsNullOrWhiteSpace(configured) || configured.Length < 3 || !char.IsAsciiLetter(configured[0]) ||
            configured[1] != ':' || configured[2] is not ('\\' or '/') || configured.IndexOf(':', 2) >= 0 ||
            configured.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("The local asset folder must be an absolute local path.", nameof(root));

        if (configured.Split(['\\', '/']).Skip(1).Any(part => part is "." or ".." || part.EndsWith('.') ||
                part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(part)))
            throw new ArgumentException("The local asset folder contains an unsafe path component.", nameof(root));

        _root = Path.GetFullPath(configured);
        if (new DriveInfo(Path.GetPathRoot(_root)!).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
            throw new ArgumentException("The local asset folder must be on a local drive.", nameof(root));
        RejectReparseAncestors(_root);
    }

    public string Root => _root;
    public string JapaneseRoot => Child("packs", PackId);
    public string StagingRoot => Child(".staging");
    public string RegistryPath => Child("installed-packs.json");
    public string SetupPreferencePath => Child("dictionary-setup.json");
    public string LockPath => Child(".dictionary-packs.lock");

    public string VersionDirectory(string version)
    {
        if (!IsSafeId(version)) throw new ArgumentException("Invalid dictionary version.", nameof(version));
        return Child("packs", PackId, version);
    }

    public string StagingDirectory(string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("Invalid operation ID.", nameof(operationId));
        return Child(".staging", operationId);
    }

    public IEnumerable<string> EnumerateStagingDirectories()
    {
        string root = StagingRoot;
        if (!Directory.Exists(root)) yield break;
        RejectReparseAncestors(root);
        foreach (string path in Directory.EnumerateDirectories(root)) yield return path;
    }

    public string Child(params string[] parts)
    {
        string path = Path.GetFullPath(Path.Combine([_root, .. parts]));
        string prefix = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Dictionary path escaped its local asset folder.");
        RejectReparseAncestors(path);
        return path;
    }

    internal static bool IsSafeId(string value) => value.Length is > 0 and <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_') && value is not ("." or "..");

    private static bool IsDeviceName(string part)
    {
        string name = part.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9');
    }

    public static void RejectReparseAncestors(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        foreach (string current in ancestors)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Dictionary path contains a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}

internal sealed record DictionaryPackRegistry(string? ActiveVersion, string? PendingVersion, bool RemovalPending, long Generation,
    string? RollbackVersion = null, string? FailedVersion = null, string? FailureMessage = null);

internal static class DictionaryPackAtomicFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        DictionaryPackPaths.RejectReparseAncestors(path);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
