using System.Runtime.InteropServices;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace Visits11.Services;

public static class QrScanner
{
    private static readonly BarcodeReaderGeneric Reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE },
        },
    };

    public static string? ScanJpeg(byte[] jpeg)
    {
        try
        {
            using var mat = Cv2.ImDecode(jpeg, ImreadModes.Grayscale);
            if (mat.Empty()) return null;
            return ScanMat(mat);
        }
        catch
        {
            return null;
        }
    }

    public static string? ScanMat(Mat gray)
    {
        try
        {
            var width = gray.Cols;
            var height = gray.Rows;
            if (width <= 0 || height <= 0) return null;
            var step = (int)gray.Step();
            var pixels = new byte[width * height];
            if (step == width)
            {
                Marshal.Copy(gray.Data, pixels, 0, pixels.Length);
            }
            else
            {
                var raw = new byte[step * height];
                Marshal.Copy(gray.Data, raw, 0, Math.Min(raw.Length, step * height));
                for (var y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(raw, y * step, pixels, y * width, width);
                }
            }
            var result = Reader.Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            return result?.Text;
        }
        catch
        {
            return null;
        }
    }
}
