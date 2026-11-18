using System.ComponentModel;
using System.Diagnostics;

internal sealed class ImageViewer(ILogger logger) : IDisposable
{
    private Process? current;

    public void Show(string imagePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            // Stop the old viewer before the new one takes over the same console.
            Dispose();
            current = Process.Start(new ProcessStartInfo("fim")
            {
                ArgumentList =
                {
                    "-o", "fb", "-d", "/dev/fb0", "-a",
                    "-c", """
                    _display_status=0;_display_busy=0;display;
                    _scale_style='';rotate90_cw;
                    if(i:swidth*_screen_height<i:sheight*_screen_width){scale 'w';}else{scale 'h';};
                    i:_want_autocenter=1;redisplay;
                    """,
                    "--", imagePath
                }
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not start fim for {ImagePath}", imagePath);
        }
    }

    public void Dispose()
    {
        if (current is null)
        {
            return;
        }

        try
        {
            if (!current.HasExited)
            {
                current.Kill();
                current.WaitForExit();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(ex, "Could not stop the previous fim process");
        }
        finally
        {
            current.Dispose();
            current = null;
        }
    }
}
