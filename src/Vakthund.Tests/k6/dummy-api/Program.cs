using System.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://+:8080");

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "Vakthund load-test dummy API",
    status = "running"
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/fast", (HttpRequest request) => Results.Ok(new
{
    ok = true,
    method = request.Method,
    path = request.Path.Value,
    query = request.QueryString.Value,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/api/data/{id:int}", (int id) => Results.Ok(new
{
    id,
    name = $"item-{id}",
    active = id % 2 == 0,
    tags = new[] { "load", "dummy", "vakthund" }
}));

app.MapGet("/api/slow", async (int? ms) =>
{
    int delayMs = Math.Clamp(ms ?? 25, 0, 500);
    Stopwatch sw = Stopwatch.StartNew();
    await Task.Delay(delayMs);

    return Results.Ok(new
    {
        requestedDelayMs = delayMs,
        actualDelayMs = sw.ElapsedMilliseconds
    });
});

app.MapPost("/api/echo", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string body = await reader.ReadToEndAsync();

    return Results.Ok(new
    {
        receivedBytes = body.Length,
        contentType = request.ContentType,
        hasAuthorization = request.Headers.ContainsKey("Authorization")
    });
});

app.Run();
