using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class ImageViewer(string cacheDirectory, ILogger logger) : IDisposable
{
    private Process? current;
    private FileStream? control;
    private string ControlPath => Path.Combine(cacheDirectory, ".fim-control");

    private static (int Width, int Height) ScreenSize()
    {
        const string dimensions = "/sys/class/graphics/fb0/virtual_size";
        if (File.Exists(dimensions))
        {
            var size = File.ReadAllText(dimensions).Split(',');
            if (size.Length == 2 && int.TryParse(size[0], out var width) &&
                int.TryParse(size[1], out var height) && width > 0 && height > 0)
            {
                return (width, height);
            }
        }

        return (1920, 1080);
    }

    public string Prepare(string sourcePath, string imageName)
    {
        var (width, height) = ScreenSize();
        var displayPath = Path.Combine(cacheDirectory, $"screen-frame-{imageName}-{width}x{height}.png");
        if (File.Exists(displayPath))
        {
            return displayPath;
        }

        var pending = displayPath + ".tmp";
        try
        {
            DisplayFrame.WriteScreenCopy(sourcePath, pending, width, height);
            using (var file = File.Open(pending, FileMode.Open, FileAccess.Write))
            {
                file.Flush(flushToDisk: true);
            }
            File.Move(pending, displayPath);
        }
        finally
        {
            File.Delete(pending);
        }

        return displayPath;
    }

    public void Show(string displayPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            if (current is { HasExited: false } && control is not null)
            {
                control.Write(System.Text.Encoding.UTF8.GetBytes(displayPath + "\n"));
                control.Flush();
                return;
            }

            Dispose();
            File.Delete(ControlPath);
            if (MakeFifo(ControlPath, 0b110_000_000) != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            // Open both ends so the server can queue a change while fim is decoding.
            control = File.Open(ControlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var start = new ProcessStartInfo("fim")
            {
                ArgumentList =
                {
                    "-o", "fb", "-d", "/dev/fb0", "-q",
                    "-c", """
                    reload;display;while(1){system 'sh' '-c' 'IFS= read -r line < "$PICTURE_MONITOR_FIM_CONTROL" && printf %s "$line"';next_image=_last_system_output;list 'clear';list 'push' next_image;goto '1';reload;display;};
                    """,
                    "--", displayPath
                }
            };
            start.Environment["PICTURE_MONITOR_FIM_CONTROL"] = ControlPath;
            current = Process.Start(start);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not start fim for {DisplayPath}", displayPath);
            Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            if (current is { HasExited: false })
            {
                current.Kill(entireProcessTree: true);
                current.WaitForExit();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(ex, "Could not stop the previous fim process");
        }
        finally
        {
            current?.Dispose();
            current = null;
            if (control is not null)
            {
                control.Dispose();
                control = null;
                File.Delete(ControlPath);
            }
        }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
