namespace FihLens.Application.Dictionary;

public enum DictionaryPackState
{
    Unavailable,
    NotInstalled,
    Installed,
    InstallPendingRestart,
    UpdatePendingRestart,
    RemovalPendingRestart,
    Failed
}

public sealed record DictionaryPackStatus(
    DictionaryPackDefinition Definition,
    DictionaryPackState State,
    string? Message = null);

public sealed record DictionaryPackProgress(string Stage, long CompletedBytes, long? TotalBytes);

public interface IDictionaryPackManager
{
    Task<IReadOnlyList<DictionaryPackStatus>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task InstallAsync(string packId, IProgress<DictionaryPackProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task ScheduleRemovalAsync(string packId, CancellationToken cancellationToken = default);
    Task<bool> GetSetupDismissedAsync(CancellationToken cancellationToken = default);
    Task SetSetupDismissedAsync(bool dismissed, CancellationToken cancellationToken = default);
}
