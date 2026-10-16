using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media;

namespace Phase1NativeCapture;

internal static class NativeCapture
{
    internal sealed class ProbeTrace
    {
        public int DevicesCreated, PoolsCreated, SessionsCreated, SessionStarts, FramesAcquired, FramesDisposed;
        public int CallbacksEntered, CallbacksExited, SubscriptionsAdded, SubscriptionsRemoved, CopiesStarted, CopiesDrained;
        public bool OwnerScopesExited;
        public int TimestampFramesRejected;
        public long? AcceptedFrameTimeSpanTicks;
    }
    [StructLayout(LayoutKind.Sequential)] internal record struct Point(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] internal record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => checked(Right - Left);
        public int Height => checked(Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
    internal record Display(nint Handle, Rect Bounds, Rect Work, uint Flags);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect bounds, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint inspectable);

    internal static Display[] Displays()
    {
        var list = new List<Display>();
        if (!EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect bounds, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref info)) return false;
            list.Add(new(monitor, info.Monitor, info.Work, info.Flags));
            return true;
        }, 0)) throw new InvalidOperationException("Monitor enumeration failed.");
        return list.OrderBy(x => x.Handle).ToArray();
    }

    private static unsafe GraphicsCaptureItem Item(nint monitor)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        nint hstring = 0, factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out hstring));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), out factory));
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            // IUnknown slots 0..2; CreateForWindow is 3, CreateForMonitor is 4.
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[4];
            Marshal.ThrowExceptionForHR(create(factory, monitor, &iid, &item));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            if (hstring != 0) WindowsDeleteString(hstring);
        }
    }

    private static IDirect3DDevice Device()
    {
        nint device = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out device, out _, out context));
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }

    internal static async Task<SoftwareBitmap> CropAsync(nint monitor, Rect crop, CancellationToken cancellation, ProbeTrace? trace = null, Action? beforeStart = null, long? renderedAfterQpc = null)
    {
        try { return await CropCoreAsync(monitor, crop, cancellation, trace, beforeStart, renderedAfterQpc); }
        finally { if (trace is not null) trace.OwnerScopesExited = true; }
    }

    private static async Task<SoftwareBitmap> CropCoreAsync(nint monitor, Rect crop, CancellationToken cancellation, ProbeTrace? trace, Action? beforeStart, long? renderedAfterQpc)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows capture unsupported.");
        var topology = Displays();
        var display = topology.Single(x => x.Handle == monitor);
        ValidateCrop(display.Bounds, crop);
        using var device = Device();
        if (trace is not null) trace.DevicesCreated++;
        var item = Item(monitor);
        if (item.Size.Width != display.Bounds.Width || item.Size.Height != display.Bounds.Height)
            throw new InvalidOperationException("Capture item dimensions differ from frozen monitor.");
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        if (trace is not null) trace.PoolsCreated++;
        using var session = pool.CreateCaptureSession(item);
        if (trace is not null) trace.SessionsCreated++;
        var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var retired = false;
        var sourceClosed = 0;
        var timestampRejections = 0;
        Direct3D11CaptureFrame? acquired = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token);
        void Arrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (trace is not null) Interlocked.Increment(ref trace.CallbacksEntered);
            try
            {
                lock (gate)
                {
                    if (retired || acquired is not null || (renderedAfterQpc.HasValue && completion.Task.IsCompleted)) return;
                    try
                    {
                        var next = sender.TryGetNextFrame();
                        if (next is null) return;
                        if (trace is not null) trace.FramesAcquired++;
                        if (renderedAfterQpc is long marker)
                        {
                            TimeSpan? frameTime;
                            try { frameTime = next.SystemRelativeTime; }
                            catch { next.Dispose(); if (trace is not null) trace.FramesDisposed++; throw; }
                            if (!FrameFreshness.IsAfter(frameTime?.Ticks, marker, System.Diagnostics.Stopwatch.Frequency))
                            {
                                next.Dispose();
                                if (trace is not null) { trace.FramesDisposed++; trace.TimestampFramesRejected++; }
                                if (++timestampRejections >= 8)
                                    completion.TrySetException(new InvalidOperationException("No timestamp-qualified frame within admission limit."));
                                return;
                            }
                            if (trace is not null) trace.AcceptedFrameTimeSpanTicks = frameTime?.Ticks;
                        }
                        if (completion.TrySetResult(next)) acquired = next;
                        else { next.Dispose(); if (trace is not null) trace.FramesDisposed++; }
                    }
                    catch (Exception e) { completion.TrySetException(e); }
                }
            }
            finally { if (trace is not null) Interlocked.Increment(ref trace.CallbacksExited); }
        }
        void Closed(GraphicsCaptureItem sender, object args)
        {
            Interlocked.Exchange(ref sourceClosed, 1);
            completion.TrySetException(new InvalidOperationException("Capture source closed."));
        }
        try
        {
            pool.FrameArrived += Arrived;
            if (trace is not null) trace.SubscriptionsAdded++;
            item.Closed += Closed;
            if (trace is not null) trace.SubscriptionsAdded++;
            using var registration = linked.Token.Register(() => completion.TrySetCanceled(linked.Token));
            beforeStart?.Invoke();
            linked.Token.ThrowIfCancellationRequested();
            session.StartCapture();
            if (trace is not null) trace.SessionStarts++;
            var frame = await completion.Task;
            CheckSource(frame);
            using var source = VideoFrame.CreateWithDirect3D11Surface(frame.Surface);
            using var destination = new VideoFrame(BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Premultiplied);
            var sourceBounds = new BitmapBounds { X = checked((uint)(crop.Left - display.Bounds.Left)), Y = checked((uint)(crop.Top - display.Bounds.Top)), Width = checked((uint)crop.Width), Height = checked((uint)crop.Height) };
            var destinationBounds = new BitmapBounds { Width = checked((uint)crop.Width), Height = checked((uint)crop.Height) };
            // Keep source/frame alive until the native copy drains, including cancellation.
            if (trace is not null) trace.CopiesStarted++;
            try { await source.CopyToAsync(destination, sourceBounds, destinationBounds); }
            finally { if (trace is not null) trace.CopiesDrained++; }
            CheckSource(frame);
            return SoftwareBitmap.Copy(destination.SoftwareBitmap);
        }
        finally
        {
            pool.FrameArrived -= Arrived;
            if (trace is not null) trace.SubscriptionsRemoved++;
            item.Closed -= Closed;
            if (trace is not null) trace.SubscriptionsRemoved++;
            lock (gate) { retired = true; }
            if (acquired is not null) { acquired.Dispose(); if (trace is not null) trace.FramesDisposed++; }
        }

        void CheckSource(Direct3D11CaptureFrame frame)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref sourceClosed) != 0) throw new InvalidOperationException("Capture source closed.");
            if (!topology.SequenceEqual(Displays()) || item.Size.Width != display.Bounds.Width || item.Size.Height != display.Bounds.Height
                || frame.ContentSize.Width != display.Bounds.Width || frame.ContentSize.Height != display.Bounds.Height)
                throw new InvalidOperationException("Frozen capture source/topology changed; reselection required.");
        }
    }

    internal static void ValidateCrop(Rect monitor, Rect crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0 || crop.Left < monitor.Left || crop.Top < monitor.Top || crop.Right > monitor.Right || crop.Bottom > monitor.Bottom)
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop must stay within its frozen monitor.");
        if (checked((long)crop.Width * crop.Height * 4) > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(crop), "Experiment crop byte limit exceeded.");
    }
}

