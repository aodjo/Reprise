using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

/// <summary>Captures only a diagnostic-owned HWND to verify the actual GPU material output.</summary>
internal static class OwnWindowCapture
{
    /// <summary>Reads one native compositor frame from the supplied owned window, never a monitor or unrelated application.</summary>
    internal static async Task SaveAsync(nint window, string path)
    {
        var item = CreateItem(window);
        var result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context);
        if (result < 0) result = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out device, out _, out context);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            var dxgiId = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgiId, out var dxgi));
            IDirect3DDevice projected;
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var native));
                try { projected = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(native); }
                finally { Marshal.Release(native); }
            }
            finally { Marshal.Release(dxgi); }
            using (projected)
            using (var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(projected, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size))
            using (var session = pool.CreateCaptureSession(item))
            {
                var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
                pool.FrameArrived += (_, _) =>
                {
                    var frame = pool.TryGetNextFrame();
                    if (frame is not null && !completion.TrySetResult(frame)) frame.Dispose();
                };
                session.IsCursorCaptureEnabled = false;
                session.StartCapture();
                using var captured = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface);
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                stream.Seek(0);
                using var output = File.Create(path);
                await stream.AsStreamForRead().CopyToAsync(output);
            }
        }
        finally { Marshal.Release(context); Marshal.Release(device); }
    }

    /// <summary>Creates a window-only capture item from the owned HWND through the documented desktop interop.</summary>
    private static unsafe GraphicsCaptureItem CreateItem(nint window)
    {
        var name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
        try
        {
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, in interopId, out var factory));
            try
            {
                var table = *(nint**)factory;
                var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)table[3];
                var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                nint item = 0;
                Marshal.ThrowExceptionForHR(create(factory, window, &itemId, &item));
                try { return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item); }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(text); }
    }

    /// <summary>Creates a native D3D device for window-only frame readback.</summary>
    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, uint driver, nint software, uint flags, nint levels,
        uint count, uint sdk, out nint device, out uint level, out nint context);
    /// <summary>Projects the D3D device into the capture API.</summary>
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint inspectable);
    /// <summary>Creates an activation-class HSTRING.</summary>
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string text, int length, out nint value);
    /// <summary>Releases the activation-class HSTRING.</summary>
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint value);
    /// <summary>Resolves the desktop capture factory.</summary>
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
}
