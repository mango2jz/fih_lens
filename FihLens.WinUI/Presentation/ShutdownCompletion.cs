namespace FihLens.WinUI.Presentation;

internal static class ShutdownCompletion
{
    internal static async Task<bool> FinalizeWithShellAsync(
        Func<Task<bool>> finalizeShared,
        Func<bool> retireShell,
        Action<Exception> reportFailure)
    {
        bool sharedComplete;
        try { sharedComplete = await finalizeShared(); }
        catch (Exception exception)
        {
            reportFailure(exception);
            sharedComplete = false;
        }

        bool shellComplete;
        try { shellComplete = retireShell(); }
        catch (Exception exception)
        {
            reportFailure(exception);
            shellComplete = false;
        }

        return sharedComplete && shellComplete;
    }

    internal static async Task DrainAsync(List<Task> work, Action<Exception> reportFailure)
    {
        while (work.Count > 0)
        {
            var snapshot = work.ToArray();
            try { await Task.WhenAll(snapshot); }
            // Inspect every terminal task below so a batch fault cannot hide sibling outcomes.
            catch (Exception) { }

            foreach (var task in snapshot)
            {
                if (task.Exception is { } failure)
                    foreach (var exception in failure.Flatten().InnerExceptions)
                        reportFailure(exception);
                work.Remove(task);
            }
        }
    }

    internal static async Task<bool> FinalizeSharedAsync(
        Task initialization,
        Func<ValueTask> disposeReader,
        Func<ValueTask> disposeSpeech,
        Action disposeClient,
        Action disposeManager,
        Action disposeCancellation,
        Action<Exception> reportFailure,
        Func<ValueTask>? drainNativeCapture = null)
    {
        var cleanupSucceeded = true;

        async Task AttemptAsync(Func<ValueTask> dispose)
        {
            try
            {
                await dispose();
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                reportFailure(exception);
            }
        }

        void Attempt(Action dispose)
        {
            try
            {
                dispose();
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                reportFailure(exception);
            }
        }

        try
        {
            await initialization;
        }
        catch (Exception exception)
        {
            reportFailure(exception);
        }

        await AttemptAsync(disposeReader);
        await AttemptAsync(disposeSpeech);
        Attempt(disposeClient);
        Attempt(disposeManager);
        Attempt(disposeCancellation);
        if (drainNativeCapture is not null) await AttemptAsync(drainNativeCapture);

        return cleanupSucceeded;
    }
}
