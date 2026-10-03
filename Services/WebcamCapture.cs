using OpenCvSharp;

namespace Visits11.Services;

public sealed class WebcamCapture : IDisposable
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<byte[]>? FrameJpeg;

    public bool Start()
    {
        try
        {
            _capture = Open(VideoCaptureAPIs.DSHOW) ?? Open(VideoCaptureAPIs.MSMF) ?? Open(VideoCaptureAPIs.ANY);
            if (_capture is null || !_capture.IsOpened()) return false;
            _capture.FrameWidth = 1280;
            _capture.FrameHeight = 720;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => Loop(token), token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static VideoCapture? Open(VideoCaptureAPIs api)
    {
        try
        {
            var capture = new VideoCapture(0, api);
            if (capture.IsOpened()) return capture;
            capture.Dispose();
        }
        catch
        {
            // следующая библиотека захвата
        }
        return null;
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var mat = new Mat();
                if (_capture is not null && _capture.Read(mat) && !mat.Empty())
                {
                    Cv2.ImEncode(".jpg", mat, out var jpeg);
                    if (jpeg.Length > 0) FrameJpeg?.Invoke(jpeg);
                }
            }
            catch
            {
                break;
            }
            Thread.Sleep(90);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* уже остановлен */ }
        try { _loop?.Wait(800); } catch { /* отмена */ }
        try { _capture?.Release(); } catch { /* камера уже закрыта */ }
        _capture?.Dispose();
        _capture = null;
        _cts?.Dispose();
    }
}
