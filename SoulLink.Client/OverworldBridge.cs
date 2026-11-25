using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SoulLink;

namespace SoulLink.Client;

internal sealed class OverworldBridge(ItemBridge items, TimeProvider clock)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(65);
    private static readonly TimeSpan MaxSampleAge = TimeSpan.FromMilliseconds(450);
    private Sample? latest;

    public void Observe(NetworkStream stream, BridgeFrame frame)
    {
        if (items.MatchesBridge(stream, frame.ItemSession, frame.Epoch) &&
            (frame.Overworld is null || frame.Overworld.IsValid()))
            Volatile.Write(ref this.latest, new Sample(frame.Epoch, frame.Overworld, clock.GetTimestamp()));
    }

    public void Reset() => Volatile.Write(ref this.latest, null);

    public async Task RunAsync(HubConnection hub, Func<bool> finished, Func<ClientPlayerView[]> players, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        bool published = false;
        bool rendered = false;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var sample = Volatile.Read(ref this.latest);
                var pose = sample is not null && clock.GetElapsedTime(sample.Received) < MaxSampleAge && !finished()
                    ? sample.Pose : null;
                if (pose is null && !published && !rendered) continue;

                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1.5));
                    var nearby = await hub.InvokeAsync<OverworldPlayer[]>("ExchangeOverworld", pose, deadline.Token) ?? [];
                    published = pose is not null;
                    if (sample is not null && (nearby.Length > 0 || rendered))
                    {
                        var current = Volatile.Read(ref this.latest);
                        var target = current?.Pose;
                        if (current?.Epoch != sample.Epoch || target is null || pose is null ||
                            clock.GetElapsedTime(current.Received) >= MaxSampleAge ||
                            target.MapGroup != pose.MapGroup || target.MapNumber != pose.MapNumber || finished())
                            nearby = [];

                        await items.SendOverworldAsync(sample.Epoch, nearby, players(), deadline.Token);
                    }

                    rendered = sample is not null && nearby.Length > 0;
                }
                catch (HubException error) when (error.Message.Contains("Method does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    // Older servers continue to support inventory tracking.
                    return;
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
                    error is HubException or InvalidOperationException or IOException or SocketException or OperationCanceledException)
                {
                    // The last pose expires on the server and the emulator independently.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private sealed record Sample(string Epoch, OverworldPose? Pose, long Received);
}
