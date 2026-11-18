using ImageMagick;

internal static class DisplayFrame
{
    public static void WriteScreenCopy(string sourcePath, string destinationPath, int screenWidth, int screenHeight)
    {
        var info = new MagickImageInfo(sourcePath);
        if (info.Width == 0 || info.Height == 0 || (long)info.Width * info.Height > 40_000_000)
        {
            throw new InvalidDataException("Image dimensions are outside the display limit.");
        }

        using var image = new MagickImage(sourcePath);
        var depth = image.Depth;
        image.AutoOrient();
        image.Rotate(90);
        image.ResetPage();

        var width = Math.Min(image.Width, (uint)Math.Max(1,
            Math.Round((double)image.Height * screenWidth / screenHeight)));
        var height = Math.Min(image.Height, (uint)Math.Max(1,
            Math.Round((double)image.Width * screenHeight / screenWidth)));
        image.Crop(new MagickGeometry((int)((image.Width - width) / 2),
            (int)((image.Height - height) / 2), width, height));
        image.ResetPage();
        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry((uint)screenWidth, (uint)screenHeight) { FillArea = true });
        image.Crop((uint)screenWidth, (uint)screenHeight, Gravity.Center);
        image.ResetPage();
        image.Depth = depth;
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", "1");
        image.Write(destinationPath, MagickFormat.Png);
    }
}
