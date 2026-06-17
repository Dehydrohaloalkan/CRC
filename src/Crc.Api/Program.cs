using Crc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CRC-32 API", Version = "v1" });
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// GET /health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health")
   .WithSummary("Health check");

// POST /crc/bytes
// Body: raw bytes → { "crc": "XXXXXXXX" }
app.MapPost("/crc/bytes", async (HttpRequest request) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    byte[] data = ms.ToArray();

    uint crc = data.Length >= 4 * 1024 * 1024
        ? Crc32.ComputeParallel(new MemoryStream(data))
        : Crc32.Compute(data);

    return Results.Ok(new { crc = crc.ToString("X8") });
})
.WithName("CrcBytes")
.WithSummary("Compute CRC-32 of raw request body");

// POST /crc/file
// Multipart form with field "file" → { "crc": "XXXXXXXX", "filename": "...", "size": N }
app.MapPost("/crc/file", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null)
        return Results.BadRequest("Form field 'file' is required");

    using var stream = file.OpenReadStream();

    uint crc = file.Length >= 4 * 1024 * 1024
        ? Crc32.ComputeParallel(stream)
        : Crc32.Compute(stream);

    return Results.Ok(new
    {
        crc      = crc.ToString("X8"),
        filename = file.FileName,
        size     = file.Length
    });
})
.WithName("CrcFile")
.WithSummary("Compute CRC-32 of an uploaded file");

app.Run();
