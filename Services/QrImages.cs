using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Visits11.Services;

public static class QrImages
{
    public static ImageSource FromText(string text)
    {
        var ecc = text.Length > 700
            ? QRCodeGenerator.ECCLevel.L
            : QRCodeGenerator.ECCLevel.M;
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(text, ecc);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(12);
        var image = new BitmapImage();
        using (var stream = new MemoryStream(bytes))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }
}
