using System.Runtime.InteropServices;
using FihLens.Application.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media;

namespace FihLens.Windows.Capture;

internal static class NativeCapture
{
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint inspectable);

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


    internal static async Task<SoftwareBitmap> CropAsync(RegionTextCaptureRequest request, long markerQpc, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException();
        var display = RequireSource(request.Source);
        using var device = Device();
        var item = Item(display.Handle);
        CheckDimensions(item.Size.Width, item.Size.Height);
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var retired = false;
        var sourceClosed = 0;
        var timestampRejections = 0;
        Direct3D11CaptureFrame? acquired = null;
        void Arrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (gate)
            {
                if (retired || acquired is not null || completion.Task.IsCompleted) return;
                try
                {
                    var next = sender.TryGetNextFrame();
                    if (next is null) return;
                    TimeSpan? frameTime;
                    try { frameTime = next.SystemRelativeTime; }
                    catch { next.Dispose(); throw; }
                    if (!FrameFreshness.IsAfter(frameTime?.Ticks, markerQpc, System.Diagnostics.Stopwatch.Frequency))
                    {
                        next.Dispose();
                        if (++timestampRejections >= 8) completion.TrySetException(new TimeoutException());
                        return;
                    }
                    if (completion.TrySetResult(next)) acquired = next;
                    else next.Dispose();
                }
                catch (Exception exception) { completion.TrySetException(exception); }
            }
        }
        void Closed(GraphicsCaptureItem sender, object args)
        {
            Interlocked.Exchange(ref sourceClosed, 1);
            completion.TrySetException(new CaptureSourceChangedException());
        }
        try
        {
            pool.FrameArrived += Arrived;
            item.Closed += Closed;
            using var registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
            CheckSource();
            session.StartCapture();
            var frame = await completion.Task.ConfigureAwait(false);
            CheckSource(frame);
            var crop = request.Crop;
            using var source = VideoFrame.CreateWithDirect3D11Surface(frame.Surface);
            using var destination = new VideoFrame(BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Premultiplied);
            var sourceBounds = new BitmapBounds
            {
                X = checked((uint)((long)crop.X - request.Source.DisplayBounds.X)),
                Y = checked((uint)((long)crop.Y - request.Source.DisplayBounds.Y)),
                Width = checked((uint)crop.Width), Height = checked((uint)crop.Height)
            };
            var destinationBounds = new BitmapBounds { Width = checked((uint)crop.Width), Height = checked((uint)crop.Height) };
            // Drain the native copy before disposing either VideoFrame, including cancellation/timeout.
            await source.CopyToAsync(destination, sourceBounds, destinationBounds);
            CheckSource(frame);
            return SoftwareBitmap.Copy(destination.SoftwareBitmap);
        }
        finally
        {
            pool.FrameArrived -= Arrived;
            item.Closed -= Closed;
            lock (gate)
            {
                retired = true;
                acquired?.Dispose();
            }
        }

        void CheckDimensions(int width, int height)
        {
            if (!CaptureValidation.FrameMatches(request.Source.DisplayBounds, width, height))
                throw new CaptureSourceChangedException();
        }
        void CheckSource(Direct3D11CaptureFrame? frame = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Volatile.Read(ref sourceClosed) != 0) throw new CaptureSourceChangedException();
            var current = RequireSource(request.Source);
            if (current.Handle != display.Handle) throw new CaptureSourceChangedException();
            CheckDimensions(item.Size.Width, item.Size.Height);
            if (frame is not null) CheckDimensions(frame.ContentSize.Width, frame.ContentSize.Height);
            if (!request.Source.DisplayBounds.Contains(request.Crop)) throw new CaptureSourceChangedException();
        }
    }

    internal static WindowsDisplayCatalog.NativeDisplay RequireSource(CaptureSourceSnapshot frozen)
    {
        var current = WindowsDisplayCatalog.Enumerate().SingleOrDefault(x => x.Display.Source.SourceId == frozen.SourceId);
        if (current is null || !CaptureValidation.SourceMatches(frozen, current.Display.Source))
            throw new CaptureSourceChangedException();
        return current;
    }
}

internal sealed class CaptureSourceChangedException : Exception;
