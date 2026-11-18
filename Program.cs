using System.Text.Json.Serialization;

const int maxImageBytes = 50_000_000;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxImageBytes);
var app = builder.Build();

var imageDirectory = Path.GetFullPath(
    builder.Configuration["PictureMonitor:ImageDirectory"] ??
    Path.Combine(AppContext.BaseDirectory, "images"));
Directory.CreateDirectory(imageDirectory);
var imageLock = new object();
using var viewer = new ImageViewer(app.Logger);
var activePath = Path.Combine(imageDirectory, ".active-image");
var activeImage = File.Exists(activePath) ? File.ReadAllText(activePath).Trim() : null;

string[] RecentImages() => Directory.EnumerateFiles(imageDirectory)
    .Select(path => Path.GetFileName(path)!)
    .Where(name => name.EndsWith(".jpg", StringComparison.Ordinal) ||
                   name.EndsWith(".png", StringComparison.Ordinal))
    .OrderDescending(StringComparer.Ordinal)
    .ToArray();

void SelectImage(string name)
{
    var pending = activePath + ".tmp";
    File.WriteAllText(pending, name);
    File.Move(pending, activePath, overwrite: true);
    activeImage = name;
    viewer.Show(Path.Combine(imageDirectory, name));
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/images", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    lock (imageLock)
    {
        return Results.Json(new MonitorState(activeImage, RecentImages().Take(5).ToArray()),
            MonitorJsonContext.Default.MonitorState);
    }
});

app.MapPost("/api/images/{name}/select", (string name, HttpContext context) =>
{
    // A custom request header prevents cross-origin HTML forms from changing the screen.
    if (!context.Request.Headers.ContainsKey("X-Picture-Monitor"))
    {
        return Results.BadRequest();
    }

    lock (imageLock)
    {
        if (!RecentImages().Take(5).Contains(name, StringComparer.Ordinal))
        {
            return Results.NotFound();
        }

        SelectImage(name);
        return Results.NoContent();
    }
});

app.MapGet("/images/{name}", (string name) =>
{
    lock (imageLock)
    {
        if (!RecentImages().Take(5).Contains(name, StringComparer.Ordinal))
        {
            return Results.NotFound();
        }

        var stream = File.Open(Path.Combine(imageDirectory, name), FileMode.Open,
            FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Results.File(stream, name.EndsWith(".png") ? "image/png" : "image/jpeg");
    }
});

app.MapPost("/upload", async (HttpContext context) =>
{
    var temporaryPath = Path.Combine(imageDirectory, $"{Guid.NewGuid():N}.tmp");
    try
    {
        string extension;
        await using (var output = File.Create(temporaryPath))
        {
            var signature = new byte[8];
            var read = await context.Request.Body.ReadAtLeastAsync(
                signature, signature.Length, throwOnEndOfStream: false, cancellationToken: context.RequestAborted);
            extension = read == 8 && signature.AsSpan().SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? ".png"
                : read == 8 && signature[0] == 255 && signature[1] == 216 && signature[2] == 255 ? ".jpg"
                : "";
            if (extension.Length == 0)
            {
                return Results.Problem("Choose a JPEG or PNG image.", statusCode: 415);
            }

            await output.WriteAsync(signature, context.RequestAborted);
            await context.Request.Body.CopyToAsync(output, context.RequestAborted);
        }

        context.RequestAborted.ThrowIfCancellationRequested();
        lock (imageLock)
        {
            var existing = RecentImages();
            var next = DateTime.UtcNow.Ticks;
            if (existing.Length > 0 &&
                long.TryParse(Path.GetFileNameWithoutExtension(existing[0]), out var previous))
            {
                next = Math.Max(next, previous + 1);
            }

            var name = $"{next:D19}{extension}";
            var savedPath = Path.Combine(imageDirectory, name);
            File.Move(temporaryPath, savedPath);
            try
            {
                SelectImage(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Delete(savedPath);
                throw;
            }
            foreach (var old in existing.Skip(4))
            {
                try
                {
                    File.Delete(Path.Combine(imageDirectory, old));
                }
                catch (IOException ex)
                {
                    app.Logger.LogWarning(ex, "Could not remove old image {Image}", old);
                }
                catch (UnauthorizedAccessException ex)
                {
                    app.Logger.LogWarning(ex, "Could not remove old image {Image}", old);
                }
            }
        }

        return Results.Text("""{"status":"Image updated successfully"}""", "application/json");
    }
    catch (BadHttpRequestException ex)
    {
        return Results.Problem("The upload could not be read.", statusCode: ex.StatusCode);
    }
    finally
    {
        File.Delete(temporaryPath);
    }
});

var startupImages = RecentImages().Take(5).ToArray();
if (!startupImages.Contains(activeImage, StringComparer.Ordinal))
{
    activeImage = startupImages.FirstOrDefault();
}

if (activeImage is { } savedImage)
{
    viewer.Show(Path.Combine(imageDirectory, savedImage));
}

app.Run();

internal sealed record MonitorState(string? ActiveImage, string[] RecentImages);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MonitorState))]
internal partial class MonitorJsonContext : JsonSerializerContext;
