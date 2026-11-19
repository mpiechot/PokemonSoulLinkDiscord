using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PokeSoulLinkBot.Core.Configuration;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Co-hosts the authenticated read-only protocol with the Discord bot.</summary>
public static class AutomationHosting
{
    /// <summary>Registers persistence and transport without starting listeners.</summary>
    public static void AddAutomation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => new AutomationStore(configuration["SoulLink:DatabasePath"] ??
            Path.Combine(AppContext.BaseDirectory, "Data", "soullink.sqlite")));
        services.AddSingleton<AutomationEngine>();
        services.AddTransient<AutomationHub>();
        services.AddSignalR(options => options.MaximumReceiveMessageSize = Protocol.MaxMessageBytes)
            .AddJsonProtocol(options => options.PayloadSerializerOptions = Protocol.Json);
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        services.AddRateLimiter(options => options.AddFixedWindowLimiter("automation", limiter =>
        {
            limiter.PermitLimit = 120;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
            limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        }));
        services.AddHostedService<AutomationNotifications>();
    }

    /// <summary>Maps minimal administration and authenticated client endpoints.</summary>
    public static void MapAutomation(this WebApplication app)
    {
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var flags = context.RequestServices.GetRequiredService<IOptions<SoulLinkOptions>>().Value;
            if ((!flags.Enabled || !flags.EnableReadTracking) &&
                (context.Request.Path.StartsWithSegments("/api/soullink") || context.Request.Path.StartsWithSegments("/soullink")))
            {
                context.Response.StatusCode = 503;
                return;
            }

            try
            {
                await next(context);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                context.Response.StatusCode = exception is UnauthorizedAccessException ? 401 : 400;
                await context.Response.WriteAsJsonAsync(new { error = exception.Message });
            }
        });
        app.MapGet("/health/soullink", (AutomationStore store) =>
        {
            store.Execute(tx => tx.Notifications());
            return Results.Ok(new { status = "ready", protocol = Protocol.Version, readOnly = true });
        });
        app.MapPost("/api/soullink/join", (JoinRequest request, AutomationEngine engine) => engine.Join(request))
            .RequireRateLimiting("automation");
        app.MapHub<AutomationHub>("/soullink", options =>
        {
            options.ApplicationMaxBufferSize = Protocol.MaxMessageBytes * 2;
            options.TransportMaxBufferSize = 4 * 1024 * 1024;
        }).RequireRateLimiting("automation");

        // This API is optional. Normal administration uses guild-restricted Discord commands.
        var admin = app.MapGroup("/api/soullink/admin").RequireRateLimiting("automation");
        admin.AddEndpointFilter(async (context, next) =>
        {
            string expected = app.Configuration["SoulLink:AdminKey"] ?? string.Empty;
            string received = context.HttpContext.Request.Headers["X-SoulLink-Admin"].ToString();
            if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(received))))
            {
                return Results.Unauthorized();
            }

            return await next(context);
        });
        admin.MapGet("/runs/{guild}", (string guild, AutomationEngine engine) => engine.Runs(guild));
        admin.MapPost("/runs", (CreateRun request, AutomationEngine engine) =>
            engine.Initialize(request.Guild, request.Channel, request.Name, request.DeathRule));
        admin.MapPost("/runs/{id:guid}/{action}", (Guid id, string action, RunAction request, AutomationEngine engine) =>
            engine.ChangeRun(request.Guild, id, action, request.Reason));
        admin.MapPost("/runs/{id:guid}/players/{player:guid}/rebaseline", (Guid id, Guid player, RunAction request, AutomationEngine engine) =>
        {
            engine.Rebaseline(request.Guild, id, player);
            return Results.Ok();
        });
    }
}
