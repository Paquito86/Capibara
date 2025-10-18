using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using System.Threading;
using System.Globalization;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Swagger UI (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Capibara API", Version = "v1" });
});

var app = builder.Build();

// lock for authorized_keys operations
var authorizedKeysLock = new SemaphoreSlim(1, 1);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Built-in OpenAPI document endpoint (optional)
    app.MapOpenApi();

    // Swagger JSON & UI
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Capibara API v1");
        // options.RoutePrefix = string.Empty; // Uncomment to expose UI at '/'
    });
}

app.UseHttpsRedirection();

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

    await authorizedKeysLock.WaitAsync();
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var existingLines = File.Exists(filePath)
            ? await File.ReadAllLinesAsync(filePath)
            : Array.Empty<string>();

        // 1) Exact match (whole line)
        if (existingLines.Any(l => string.Equals(l.Trim(), key, StringComparison.Ordinal)))
        {
            return Results.Conflict("La clave ya existe (coincidencia exacta).");
        }

        // 2) Compare ignoring trailing comment (after the second space)
        var incomingPrefix = GetTwoTokenPrefix(key);
        if (incomingPrefix is null)
        {
            return Results.BadRequest("Formato de clave SSH inválido.");
        }

        var sameContentExists = existingLines.Any(l =>
        {
            var prefix = GetTwoTokenPrefix(l);
            return prefix is not null && string.Equals(prefix, incomingPrefix, StringComparison.Ordinal);
        });

        if (sameContentExists)
        {
            return Results.Conflict("Ya existe una clave idéntica (aunque con distinto nombre).");
        }

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
    }
    finally
    {
        authorizedKeysLock.Release();
    }

    return Results.Created("/ssh/keys", null);
})
.Accepts<string>("text/plain")
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status409Conflict)
.WithOpenApi(op =>
{
    op.Summary = "Registrar clave pública SSH";
    op.Description = "Recibe una clave pública SSH en texto plano (una por línea) y la agrega a `Files/authorized_keys`. Si la clave ya existe (igual o con el mismo contenido ignorando el nombre), devuelve 409.";

    // Document request body as text/plain with an example
    op.RequestBody ??= new Microsoft.OpenApi.Models.OpenApiRequestBody();
    op.RequestBody.Required = true;
    op.RequestBody.Content.Clear();
    op.RequestBody.Content["text/plain"] = new Microsoft.OpenApi.Models.OpenApiMediaType
    {
        Example = new OpenApiString("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIL7p14I6jkXQeRrB74dcGSG9evn+ItVpmxnhWI77CUc/ eddsa-key-test03")
    };

    return op;
});

// Helper: take first two tokens (type and base64), dropping the optional trailing comment
static string? GetTwoTokenPrefix(string line)
{
    if (string.IsNullOrWhiteSpace(line)) return null;
    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2) return null;
    return parts[0] + " " + parts[1];
}

app.MapGet("/logs/mssql", async (HttpContext context, string? since) =>
{
    if (string.IsNullOrWhiteSpace(since))
    {
        return Results.BadRequest("Debe proporcionar el parámetro 'since' (fecha/hora). Ejemplos: 2025-10-18T13:00:00Z o 2025-10-18 13:00:00");
    }

    if (!TryParseSince(since, out var sinceInstant))
    {
        return Results.BadRequest("Formato de fecha/hora inválido para 'since'. Use ISO 8601, p. ej.: 2025-10-18T13:00:00Z o 2025-10-18 13:00:00");
    }

    var logPath = Path.Combine(app.Environment.ContentRootPath, "Files", "backup_mssql.log");
    if (!File.Exists(logPath))
    {
        return Results.Ok(Array.Empty<LogEntry>());
    }

    var lines = await File.ReadAllLinesAsync(logPath);
    var list = new List<LogEntry>(capacity: lines.Length);

    foreach (var line in lines)
    {
        if (TryParseLogLine(line, out var entry))
        {
            if (entry.Timestamp > sinceInstant)
            {
                list.Add(entry);
            }
        }
    }

    // order ascending by time for readability
    list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

    return Results.Ok(list);
})
.Produces<IEnumerable<LogEntry>>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.WithOpenApi(op =>
{
    op.Summary = "Obtener logs de MSSQL posteriores a una fecha";
    op.Description = "Lee `Files/backup_mssql.log` y devuelve en JSON las entradas cuya marca de tiempo es posterior a 'since'. El parámetro 'since' acepta formatos ISO 8601 (ej.: 2025-10-18T13:00:00Z) o 'yyyy-MM-dd HH:mm:ss'.";
    return op;
});

static bool TryParseSince(string input, out DateTimeOffset value)
{
    // Primero intentar DateTimeOffset (permite Z/offset)
    if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
    {
        value = dto;
        return true;
    }

    // Intentar formato sin offset, asumir hora local
    if (DateTime.TryParseExact(input, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
    {
        value = new DateTimeOffset(dt);
        return true;
    }

    value = default;
    return false;
}

static bool TryParseLogLine(string line, out LogEntry entry)
{
    entry = default;
    if (string.IsNullOrWhiteSpace(line)) return false;

    var m = LogParsing.LogLineRegex.Match(line);
    if (!m.Success) return false;

    if (!DateTime.TryParseExact(m.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
    {
        return false;
    }

    var level = m.Groups["level"].Value;
    var msg = m.Groups["msg"].Value;

    entry = new LogEntry(new DateTimeOffset(ts), level, msg);
    return true;
}

app.Run();

public readonly record struct LogEntry(DateTimeOffset Timestamp, string Level, string Message);

file static class LogParsing
{
    // Regex to parse lines like: 2025-10-18 13:05:04 level=INFO message="..."
    internal static readonly Regex LogLineRegex = new(
        pattern: "^(?<ts>\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2})\\s+level=(?<level>\\w+)\\s+message=\"(?<msg>.*)\"$",
        options: RegexOptions.Compiled);
}