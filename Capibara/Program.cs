using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using System.Threading;

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

app.Run();