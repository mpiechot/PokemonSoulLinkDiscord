using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Coalesces run changes and invalidates the views of authenticated subscribers.</summary>
public sealed class RunChangeBroadcast : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> pending = new ConcurrentDictionary<Guid, byte>();
    private readonly ConcurrentDictionary<Guid, string> peers = new ConcurrentDictionary<Guid, string>();
    private readonly AutomationStore store;
    private readonly IHubContext<AutomationHub> hubs;
    private readonly ILogger<RunChangeBroadcast> logger;

    /// <summary>Initializes the committed-change broadcaster.</summary>
    public RunChangeBroadcast(AutomationStore store, IHubContext<AutomationHub> hubs, ILogger<RunChangeBroadcast> logger)
    {
        this.store = store;
        this.hubs = hubs;
        this.logger = logger;
    }

    internal static string Group(Guid id) => $"run:{id:N}";

    internal void Notify(Guid id) => this.pending.TryAdd(id, 0);

    internal void Register(Guid player, string connection) => this.peers[player] = connection;

    internal void Unregister(Guid player, string connection) =>
        this.peers.TryRemove(new KeyValuePair<Guid, string>(player, connection));

    internal string? Peer(Guid player) => this.peers.GetValueOrDefault(player);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.store.RunChanged += this.Notify;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var sending = new List<Task>();
                foreach (var entry in this.pending)
                {
                    if (this.pending.TryRemove(entry.Key, out _))
                    {
                        sending.Add(this.hubs.Clients.Group(Group(entry.Key)).SendAsync("RunChanged", stoppingToken));
                    }
                }

                try
                {
                    await Task.WhenAll(sending);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    this.logger.LogWarning(error, "A run update notification could not be sent.");
                }
            }
        }
        finally
        {
            this.store.RunChanged -= this.Notify;
        }
    }
}
