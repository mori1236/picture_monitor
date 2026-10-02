using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/upload", async (HttpContext context) =>
{
  try
  {
    string filePath = "/home/orangepi/current_image.jpg";

    using (var fileStream = File.Create(filePath))
    {
      await context.Request.Body.CopyToAsync(fileStream);
    } 

    Process.Start(new ProcessStartInfo
    {
      FileName = "feh",
      Arguments = $"--reload 1 --fullscreen --hide-pointer {filePath}"
    });

    return Results.Ok(new { status = "Image updated successfully" });
  }
  catch (Exception ex)
  {
    return Results.Problem(ex.Message);
  }
});

app.Run();
