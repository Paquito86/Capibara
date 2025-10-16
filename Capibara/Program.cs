var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Endpoint to receive and store SSH public keys (one per line) in Files/authorized_keys
app.MapPost("/ssh/keys", async (HttpContext context) =>
{
    // Read raw body as text
    using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var body = await reader.ReadToEndAsync();
    var key = body?.Trim();

    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest("La clave pública no puede estar vacía.");
    }

    // Must be a single line (one key per line)
    if (key.Contains('\n') || key.Contains('\r'))
    {
        return Results.BadRequest("La clave debe ser de una sola línea.");
    }

    // Optional minimal shape check to avoid garbage (common SSH key prefixes)
    // Accepts: ssh-ed25519, ssh-rsa, ecdsa-sha2-nistp{256,384,521}
    var isLikelySshKey = key.StartsWith("ssh-ed25519 ", StringComparison.Ordinal) ||
                         key.StartsWith("ssh-rsa ", StringComparison.Ordinal) ||
                         key.StartsWith("ecdsa-sha2-nistp256 ", StringComparison.Ordinal) ||
                         key.StartsWith("ecdsa-sha2-nistp384 ", StringComparison.Ordinal) ||
                         key.StartsWith("ecdsa-sha2-nistp521 ", StringComparison.Ordinal);
    if (!isLikelySshKey)
    {
        return Results.BadRequest("Formato de clave SSH no reconocido.");
    }

    // Resolve authorized_keys path under the app content root
    var filePath = Path.Combine(app.Environment.ContentRootPath, "Files", "authorized_keys");

    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

    // Ensure each key is on its own line; prepend a newline if the file exists and doesn't end with one
    var needsLeadingNewline = false;
    var fileInfo = new FileInfo(filePath);
    if (fileInfo.Exists && fileInfo.Length > 0)
    {
        using var fsRead = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fsRead.Length > 0)
        {
            fsRead.Seek(-1, SeekOrigin.End);
            int last = fsRead.ReadByte();
            needsLeadingNewline = last != (int)'\n';
        }
    }

    // Append atomically with UTF-8 (no BOM)
    using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
    using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        if (needsLeadingNewline)
        {
            await writer.WriteLineAsync();
        }
        await writer.WriteLineAsync(key);
        await writer.FlushAsync();
    }

    return Results.Created("/ssh/keys", null);
});

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
