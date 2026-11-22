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
    SafariBall = 5,
    NetBall = 6,
    DiveBall = 7,
    NestBall = 8,
    RepeatBall = 9,
    TimerBall = 10,
    LuxuryBall = 11,
    PremierBall = 12,
    RareCandy = 68,
}

internal sealed class ItemBridge
{
    private readonly object gate = new();
    private string? session;
    private ReadyBridge? ready;
    private LoadBridge? loadBridge;
    private PendingGrant? pending;

    public string Begin()
    {
        lock (this.gate)
        {
            this.ready = null;
            this.loadBridge = null;
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

    public bool CanSave
    {
        get
        {
            lock (this.gate) return this.CurrentBridge() is not null && this.pending is null;
        }
    }

    public bool CanLoad
    {
        get
        {
            lock (this.gate) return this.loadBridge is not null && this.pending is null;
        }
    }

    public bool MatchesSession(string? value)
    {
        lock (this.gate) return this.session is not null && value == this.session;
    }

    public void ObserveReady(NetworkStream stream, BridgeFrame frame)
    {
        lock (this.gate)
        {
            if (this.session is { } expected && frame.ItemSession == expected && IsEpoch(frame.Epoch))
                this.loadBridge = new LoadBridge(stream, expected, frame.Epoch);
        }
    }

    public void Observe(NetworkStream stream, BridgeFrame frame)
    {
        lock (this.gate)
        {
            this.ready = this.session is { } expected && frame.ItemSession == expected &&
                frame.LayoutId == "ruby-axvd-reference-v2" && frame.TrainerId is uint trainer &&
                frame.Pokemon.Any(p => p.Container == Placement.Party) &&
                IsEpoch(frame.Epoch)
                ? new ReadyBridge(stream, expected, frame.Epoch, trainer, frame.InBattle, DateTimeOffset.UtcNow)
                : null;
        }
    }

    public void Disconnect(NetworkStream stream)
    {
        lock (this.gate)
        {
            if (this.ready?.Stream == stream) this.ready = null;
            if (this.loadBridge?.Stream == stream) this.loadBridge = null;
            if (this.pending is { } request && request.Stream == stream)
                request.Completion.TrySetException(new IOException("Die Verbindung zu mGBA wurde unterbrochen."));
        }
    }

    public void AcceptResult(NetworkStream stream, BridgeFrame frame)
    {
        lock (this.gate)
        {
            if (this.pending is { } request && request.Stream == stream && request.Id == frame.ItemRequest &&
                frame.ItemSession == request.Session && frame.ItemResult is { } result)
            {
                if (request.Action == "LOAD" && result == "ok")
                {
                    if (this.loadBridge?.Stream == stream) this.loadBridge = null;
                    if (this.ready?.Stream == stream) this.ready = null;
                }
                request.Completion.TrySetResult(new GrantResult(result, frame.ItemQuantity));
            }
        }
    }

    public async Task<int> AddAsync(PcItem item, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(item)) throw new ArgumentOutOfRangeException(nameof(item));
        var outcome = await this.SendAsync("PC", (int)item, cancellationToken);
        if (outcome is { Status: "ok", Quantity: >= 99 and <= 49950 }) return outcome.Quantity.Value;
        throw new InvalidOperationException(outcome.Status switch
        {
            "full" => "Für 99 Stück ist im Item-Lager zu wenig Platz.",
            "storage" => "Das Item-Lager dieses Spielstands konnte nicht sicher gelesen werden.",
            "location" => "Schließe das Spielmenü und kehre ins Spielfeld zurück.",
            _ => "Spielstand oder Verbindung hat sich geändert. Prüfe das Item-Lager.",
        });
    }

    public async Task SaveSlotOneAsync(CancellationToken cancellationToken)
    {
        var outcome = await this.SendAsync("SAVE", 1, cancellationToken);
        if (outcome.Status != "ok") throw new IOException("mGBA konnte Slot 1 nicht speichern.");
    }

    public async Task LoadSlotOneAsync(CancellationToken cancellationToken)
    {
        var outcome = await this.SendAsync("LOAD", 1, cancellationToken);
        if (outcome.Status != "ok") throw new IOException(outcome.Status == "load"
            ? "Slot 1 ist leer oder konnte nicht geladen werden."
            : "mGBA konnte Slot 1 nicht laden.");
    }

    private async Task<GrantResult> SendAsync(string action, int value, CancellationToken cancellationToken)
    {
        string epoch;
        uint trainer;
        PendingGrant request;
        lock (this.gate)
        {
            if (this.pending is not null) throw new InvalidOperationException("mGBA verarbeitet noch die vorherige Anfrage.");
            if (action == "LOAD")
            {
                var target = this.loadBridge ?? throw new InvalidOperationException("mGBA ist noch nicht zum Laden bereit.");
                (epoch, trainer) = (target.Epoch, 0);
                request = new PendingGrant(target.Stream, target.Session, action, Guid.NewGuid(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            }
            else
            {
                var target = this.CurrentBridge() ?? throw new InvalidOperationException("Es sind keine aktuellen Spieldaten aus mGBA verfügbar.");
                if (action == "PC" && target.InBattle)
                    throw new InvalidOperationException("Verlasse den Kampf, bevor du Items hinzufügst.");
                (epoch, trainer) = (target.Epoch, target.Trainer);
                request = new PendingGrant(target.Stream, target.Session, action, Guid.NewGuid(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            }
            this.pending = request;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            long expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5;
            string packet = FormattableString.Invariant($"{action} {request.Session} {epoch} {trainer} {request.Id:D} {value} {expires}\n");
            await request.Stream.WriteAsync(Encoding.ASCII.GetBytes(packet), timeout.Token);
            return await request.Completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Keine Bestätigung von mGBA erhalten.");
        }
        finally
        {
            lock (this.gate) this.pending = null;
        }
    }

    private ReadyBridge? CurrentBridge() =>
        this.ready is { } bridge && DateTimeOffset.UtcNow - bridge.Seen < TimeSpan.FromSeconds(4) ? bridge : null;

    private static bool IsEpoch(string? epoch) =>
        epoch is { Length: > 0 and <= 48 } && epoch.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private sealed record ReadyBridge(NetworkStream Stream, string Session, string Epoch, uint Trainer, bool InBattle, DateTimeOffset Seen);
    private sealed record LoadBridge(NetworkStream Stream, string Session, string Epoch);

    private sealed record GrantResult(string Status, int? Quantity);

    private sealed record PendingGrant(NetworkStream Stream, string Session, string Action, Guid Id, TaskCompletionSource<GrantResult> Completion);
}
