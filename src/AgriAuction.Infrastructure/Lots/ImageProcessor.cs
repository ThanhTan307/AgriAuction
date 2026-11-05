using AgriAuction.Application.Lots;
using AgriAuction.Domain.Catalog;
using SkiaSharp;

namespace AgriAuction.Infrastructure.Lots;

public sealed class ImageProcessor : IImageProcessor
{
    private static RuleViolation Invalid(string message) => new(new() { ["file"] = [message] });
    public async Task<ImagePayload> DecodeAsync(Stream input, long length, string fileName, string contentType, CancellationToken ct)
    {
        if (length is < 1 or > ImageLimits.MaxBytes) throw Invalid("Ảnh cần từ 1 byte đến 5 MiB.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var expected = extension == ".png" && contentType == "image/png" ? SKEncodedImageFormat.Png
            : extension is ".jpg" or ".jpeg" && contentType == "image/jpeg" ? SKEncodedImageFormat.Jpeg
            : throw Invalid("Chỉ nhận ảnh JPEG/PNG đúng phần mở rộng và MIME; không SVG/GIF/PDF.");
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ImageLimits.MaxBytes) throw Invalid("Ảnh vượt 5 MiB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length != length) throw Invalid("Độ dài ảnh không khớp dữ liệu nhận được.");
        using var data = SKData.CreateCopy(buffer.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != expected) throw Invalid("Nội dung không phải ảnh JPEG/PNG hợp lệ.");
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width < 1 || height < 1 || width > ImageLimits.MaxDimension || height > ImageLimits.MaxDimension || (long)width * height > ImageLimits.MaxPixels)
            throw Invalid("Ảnh tối đa 6000 px mỗi chiều và 16 triệu pixel.");
        if (codec.FrameCount > 1) throw Invalid("Chỉ nhận ảnh tĩnh một khung hình.");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw Invalid("Không giải mã đầy đủ được ảnh; ảnh có thể hỏng/cắt cụt.");
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var normalized = new SKBitmap(swap ? height : width, swap ? width : height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);
            var (a, b, c, d, e, f) = codec.EncodedOrigin switch
            {
                SKEncodedOrigin.TopRight => (-1f, 0f, (float)width, 0f, 1f, 0f),
                SKEncodedOrigin.BottomRight => (-1f, 0f, (float)width, 0f, -1f, (float)height),
                SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, (float)height),
                SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),
                SKEncodedOrigin.RightTop => (0f, -1f, (float)height, 1f, 0f, 0f),
                SKEncodedOrigin.RightBottom => (0f, -1f, (float)height, -1f, 0f, (float)width),
                SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, (float)width),
                _ => (1f, 0f, 0f, 0f, 1f, 0f)
            };
            canvas.SetMatrix(new SKMatrix { ScaleX = a, SkewX = b, TransX = c, SkewY = d, ScaleY = e, TransY = f, Persp2 = 1 });
            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var image = SKImage.FromBitmap(normalized);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        // Persist only re-encoded raster bytes. Original names, EXIF/GPS and
        // appended payloads never become served content or filesystem paths.
        return new(encoded.ToArray(), normalized.Width, normalized.Height);
    }
}
