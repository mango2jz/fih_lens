using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace FihLens.Windows.Startup;

public enum SecondaryActivationWaitResult { Completed, TimedOut, Failed }

public static class SecondaryActivationWait
{
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint WaitTimeoutMilliseconds = 5000;
    private const uint DispatchCallsAndWindowMessages = 0x00000003;
    private const int RpcCallPending = unchecked((int)0x80010115);

    public static SecondaryActivationWaitResult Redirect(Func<Task> redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        SafeWaitHandle waiter = CreateEvent(IntPtr.Zero, true, false, null);
        if (waiter.IsInvalid)
        {
            waiter.Dispose();
            return SecondaryActivationWaitResult.Failed;
        }

        try
        {
            if (!DuplicateHandle(GetCurrentProcess(), waiter, GetCurrentProcess(), out nint duplicate,
                    0, false, DuplicateSameAccess))
                return SecondaryActivationWaitResult.Failed;
            SafeWaitHandle workerHandle = new(duplicate, ownsHandle: true);
            int workerResult = (int)SecondaryActivationWaitResult.Failed;

            Task worker;
            try
            {
                worker = Task.Run(async () =>
                {
                    try
                    {
                        await redirect().ConfigureAwait(false);
                        Volatile.Write(ref workerResult, (int)SecondaryActivationWaitResult.Completed);
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Debug.WriteLine($"Activation redirect exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
                    }
                    finally
                    {
                        try
                        {
                            if (!SetEvent(workerHandle.DangerousGetHandle()))
                            {
                                int error = Marshal.GetLastWin32Error();
                                Volatile.Write(ref workerResult, (int)SecondaryActivationWaitResult.Failed);
                                System.Diagnostics.Debug.WriteLine($"Activation wait stage=signal win32Error={error}");
                            }
                        }
                        finally { workerHandle.Dispose(); }
                    }
                });
            }
            catch
            {
                workerHandle.Dispose();
                return SecondaryActivationWaitResult.Failed;
            }

            _ = worker.ContinueWith(task =>
            {
                if (task.IsFaulted) _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            int hr = CoWaitForMultipleObjects(DispatchCallsAndWindowMessages, WaitTimeoutMilliseconds,
                1, [waiter.DangerousGetHandle()], out _);
            if (hr == RpcCallPending) return SecondaryActivationWaitResult.TimedOut;
            if (hr != 0) return SecondaryActivationWaitResult.Failed;
            return (SecondaryActivationWaitResult)Volatile.Read(ref workerResult);
        }
        finally
        {
            waiter.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateEvent(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(nint sourceProcess, SafeWaitHandle sourceHandle, nint targetProcess,
        out nint targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetEvent(nint handle);
    [DllImport("ole32.dll")] private static extern int CoWaitForMultipleObjects(uint flags, uint timeout,
        uint count, [In] nint[] handles, out uint index);
}
