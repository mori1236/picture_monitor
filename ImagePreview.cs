using SkiaSharp;

internal static class ImagePreview
{
    public static byte[]? Create(string path, int maximumEdge)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > 40_000_000)
        {
            return null;
        }

        var origin = codec.EncodedOrigin;
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = swapsAxes ? codec.Info.Height : codec.Info.Width;
        var orientedHeight = swapsAxes ? codec.Info.Width : codec.Info.Height;
        var scale = Math.Min(1.0, (double)maximumEdge / Math.Max(orientedWidth, orientedHeight));
        var decodeSize = codec.GetScaledDimensions((float)Math.Min(1, scale * 2));
        using var decoded = new SKBitmap(new SKImageInfo(decodeSize.Width, decodeSize.Height));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
        {
            return null;
        }

        var width = (float)(codec.Info.Width * scale);
        var height = (float)(codec.Info.Height * scale);
        var outputWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));
        using var output = new SKBitmap(outputWidth, outputHeight);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Black);
        canvas.Translate(output.Width / 2f, output.Height / 2f);
        var rotation = origin switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom => 90,
            SKEncodedOrigin.LeftBottom => -90,
            _ => 0
        };
        canvas.RotateDegrees(rotation);
        canvas.Scale(
            origin is SKEncodedOrigin.TopRight or SKEncodedOrigin.RightBottom ? -1 : 1,
            origin is SKEncodedOrigin.BottomLeft or SKEncodedOrigin.LeftTop ? -1 : 1);
        using var image = SKImage.FromBitmap(decoded);
        canvas.DrawImage(image, new SKRect(-width / 2f, -height / 2f, width / 2f, height / 2f),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        canvas.Flush();
        using var data = output.Encode(SKEncodedImageFormat.Jpeg, 78);
        return data?.ToArray();
    }
}
