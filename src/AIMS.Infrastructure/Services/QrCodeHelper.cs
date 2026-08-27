using QRCoder;

namespace AIMS.Infrastructure.Services;

public static class QrCodeHelper
{
    public static byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(pixelsPerModule);
    }
}
