using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SoulLink;

namespace SoulLink.Client;

public enum PcItem
{
    MasterBall = 1,
    UltraBall = 2,
    GreatBall = 3,
    PokeBall = 4,
    RareCandy = 68,
}

internal sealed class ItemBridge
{
    private readonly object gate = new();
    private string? session;
    private ReadyBridge? ready;
    private PendingGrant? pending;

    public string Begin()
    {
        lock (this.gate)
        {
            this.ready = null;
            return this.session = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        }
    }

    public bool Ready
    {
        get
        {
            lock (this.gate) return this.CurrentBridge() is { InBattle: false };
        }
    }

    public bool MatchesSession(string? value)
    {
        lock (this.gate) return this.session is not null && value == this.session;
    }

    public void Observe(NetworkStream stream, BridgeFrame frame)
    {
        lock (this.gate)
        {
            this.ready = this.session is { } expected && frame.ItemSession == expected &&
                frame.LayoutId == "ruby-axvd-reference-v2" && frame.TrainerId is uint trainer &&
                frame.Pokemon.Any(p => p.Container == Placement.Party) &&
                frame.Epoch is { Length: > 0 and <= 48 } epoch && epoch.All(char.IsAsciiLetterOrDigit)
                ? new ReadyBridge(stream, expected, epoch, trainer, frame.InBattle, DateTimeOffset.UtcNow)
                : null;
        }
    }

    public void Disconnect(NetworkStream stream)
    {
        lock (this.gate)
        {
            if (this.ready?.Stream == stream) this.ready = null;
            if (this.pending is { } request && request.Stream == stream)
                request.Completion.TrySetException(new IOException("Verbindung unterbrochen. Prüfe das Item-Lager vor einem weiteren Klick."));
        }
    }

    public void AcceptResult(NetworkStream stream, BridgeFrame frame)
    {
        lock (this.gate)
        {
            if (this.pending is { } request && request.Stream == stream && request.Id == frame.ItemRequest &&
                frame.ItemSession == request.Session && frame.ItemResult is { } result)
                request.Completion.TrySetResult(new GrantResult(result, frame.ItemQuantity));
        }
    }

    public async Task<int> AddAsync(PcItem item, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(item)) throw new ArgumentOutOfRangeException(nameof(item));
        ReadyBridge target;
        PendingGrant request;
        lock (this.gate)
        {
            target = this.CurrentBridge() is { InBattle: false } bridge ? bridge :
                throw new InvalidOperationException("Lade einen Rubin-Spielstand und verlasse den Kampf.");
            if (this.pending is not null) throw new InvalidOperationException("Die letzte Zugabe wird noch verarbeitet.");
            request = new PendingGrant(target.Stream, target.Session, Guid.NewGuid(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            this.pending = request;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            long expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5;
            string packet = FormattableString.Invariant($"PC {target.Session} {target.Epoch} {target.Trainer} {request.Id:D} {(int)item} {expires}\n");
            await target.Stream.WriteAsync(Encoding.ASCII.GetBytes(packet), timeout.Token);
            var outcome = await request.Completion.Task.WaitAsync(timeout.Token);
            if (outcome is { Status: "ok", Quantity: >= 99 and <= 49950 }) return outcome.Quantity.Value;
            throw new InvalidOperationException(outcome.Status switch
            {
                "full" => "Für 99 Stück ist im Item-Lager zu wenig Platz.",
                "storage" => "Das Item-Lager dieses Spielstands konnte nicht sicher gelesen werden.",
                "location" => "Schließe das Spielmenü und kehre ins Spielfeld zurück.",
                _ => "Spielstand oder Verbindung hat sich geändert. Prüfe das Item-Lager.",
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Keine Bestätigung von mGBA. Prüfe das Item-Lager vor einem weiteren Klick.");
        }
        finally
        {
            lock (this.gate) this.pending = null;
        }
    }

    private ReadyBridge? CurrentBridge() =>
        this.ready is { } bridge && DateTimeOffset.UtcNow - bridge.Seen < TimeSpan.FromSeconds(4) ? bridge : null;

    private sealed record ReadyBridge(NetworkStream Stream, string Session, string Epoch, uint Trainer, bool InBattle, DateTimeOffset Seen);

    private sealed record GrantResult(string Status, int? Quantity);

    private sealed record PendingGrant(NetworkStream Stream, string Session, Guid Id, TaskCompletionSource<GrantResult> Completion);
}
