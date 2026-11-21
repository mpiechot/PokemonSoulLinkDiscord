using System.Net;
using SoulLink;
using SoulLink.Client;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{builder.Configuration.GetValue("UiPort", 5090)}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<ClientRuntime>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ClientRuntime>());
var app = builder.Build();
app.Use(async (context, next) =>
{
    // A malicious website must not be able to register this loopback client via a browser.
    string origin = context.Request.Headers.Origin.ToString();
    string expected = $"http://127.0.0.1:{builder.Configuration.GetValue("UiPort", 5090)}";
    if (context.Request.Host.Host != "127.0.0.1" || (origin.Length > 0 && origin != expected))
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    try { await next(context); }
    catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or InvalidOperationException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = exception.Message });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/status", (ClientRuntime client) => client.Status());
app.MapPost("/api/join", async (JoinForm form, ClientRuntime client) =>
{
    await client.JoinAsync(form);
    return Results.Ok();
});
app.MapPost("/api/reconnect", async (ClientRuntime client) =>
{
    await client.ReconnectAsync();
    return Results.Ok();
});
Console.WriteLine($"SoulLink client: http://127.0.0.1:{builder.Configuration.GetValue("UiPort", 5090)}");
await app.RunAsync();
