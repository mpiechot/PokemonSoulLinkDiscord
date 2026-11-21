using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokeSoulLinkBot.Core.Configuration;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Dispatches durable notifications without blocking observation acknowledgements.</summary>
public sealed class AutomationNotifications : BackgroundService
{
    private readonly AutomationStore store;
    private readonly DiscordSocketClient discord;
    private readonly IOptions<SoulLinkOptions> options;
    private readonly ILogger<AutomationNotifications> logger;

    /// <summary>Initializes the durable notification dispatcher.</summary>
    public AutomationNotifications(
        AutomationStore store,
        DiscordSocketClient discord,
        IOptions<SoulLinkOptions> options,
        ILogger<AutomationNotifications> logger)
    {
        this.store = store;
        this.discord = discord;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!this.options.Value.Enabled || !this.options.Value.EnableDiscordEvents || this.discord.ConnectionState != ConnectionState.Connected)
            {
                continue;
            }

            foreach (var notification in this.store.Execute(tx => tx.Notifications()))
            {
                try
                {
                    if (await this.discord.GetChannelAsync(notification.ChannelId) is not IMessageChannel channel)
                    {
                        continue;
                    }

                    string message = notification.Message.Length > 1800 ? notification.Message[..1800] : notification.Message;
                    await channel.SendMessageAsync(
                        message,
                        allowedMentions: AllowedMentions.None,
                        options: new RequestOptions { CancelToken = stoppingToken });
                    this.store.Execute(tx =>
                    {
                        tx.Delivered(notification.Id);
                        return true;
                    });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    this.logger.LogWarning(
                        "Discord delivery failed for event {EventId}, run {RunId}: {FailureType}",
                        notification.Id,
                        notification.RunId,
                        exception.GetType().Name);
                }
            }
        }
    }
}
