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

public enum ItemDestination { PcStorage, Bag }

internal sealed class ItemBridge
{
    private readonly object gate = new();
    private readonly SemaphoreSlim writeGate = new(1);
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
            lock (this.gate) return this.CurrentBridge() is { InBattle: false, HasParty: true };
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

    public bool MatchesBridge(NetworkStream stream, string? token, string epoch)
    {
        lock (this.gate) return this.loadBridge is { } bridge &&
            bridge.Stream == stream && bridge.Session == token && bridge.Epoch == epoch;
    }

    public Task SendOverworldAsync(string epoch, OverworldPlayer[] players, ClientPlayerView[] roster, CancellationToken cancellationToken)
    {
        LoadBridge? target;
        lock (this.gate) target = this.loadBridge;
        if (target is null || target.Epoch != epoch) return Task.CompletedTask;

        var packets = players.Take(11).Where(player => player.Pose.IsValid()).Select(player =>
        {
            var pose = player.Pose;
            string packet = FormattableString.Invariant($"{player.PlayerId:N}:{pose.MapGroup}:{pose.MapNumber}:{pose.X}:{pose.Y}:{pose.OffsetX}:{pose.OffsetY}:{pose.Graphics}:{pose.Image}:{pose.Flip}:{pose.Elevation}:{pose.Priority}:{pose.Subsprite}:{pose.SurfImage}:{pose.SurfOffsetY}:{pose.SurfFlip}");
            int index = Array.FindIndex(roster, entry => entry.Id == player.PlayerId);
            if (index < 0) return packet;
            var appearance = roster[index];
            int color = PlayerColors.Resolve(index, appearance.Color);
            return FormattableString.Invariant($"{packet}:{color:X6}:{PixelLabel(appearance.Name)}");
        });
        string packet = $"AVATARS {target.Session} {epoch} {string.Join(' ', packets)}\n";
        return this.WriteAsync(target.Stream, packet, cancellationToken);
    }

    public async Task<bool> SendOverworldStyleAsync(NetworkStream stream, string? token, string epoch, int? color,
        bool showLabels, CancellationToken cancellationToken)
    {
        if (!this.MatchesBridge(stream, token, epoch)) return false;
        string tint = color?.ToString("X6", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        await this.WriteAsync(stream, $"AVATARSTYLE {token} {epoch} {tint} {(showLabels ? 1 : 0)}\n", cancellationToken);
        return true;
    }

    internal static string PixelLabel(string name)
    {
        var output = new StringBuilder(12);
        foreach (var rune in name.EnumerateRunes().Select(Rune.ToUpperInvariant))
        {
            char glyph = rune.Value switch
            {
                >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-' or '_' or '.' or 'Ä' or 'Ö' or 'Ü' or 'ß' => (char)rune.Value,
                _ => '?',
            };
            output.Append(glyph);
            if (output.Length == 12) break;
        }
        return Convert.ToHexString(Encoding.Latin1.GetBytes(output.ToString().TrimEnd()));
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
                IsEpoch(frame.Epoch)
                ? new ReadyBridge(stream, expected, frame.Epoch, trainer, frame.InBattle,
                    frame.Pokemon.Any(p => p.Container == Placement.Party), DateTimeOffset.UtcNow)
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
                request.Completion.TrySetResult(new GrantResult(result, frame.ItemQuantity, frame.TradePokemon));
            }
        }
    }

    public async Task<int> AddAsync(PcItem item, ItemDestination destination, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(item)) throw new ArgumentOutOfRangeException(nameof(item));
        if (!Enum.IsDefined(destination)) throw new ArgumentOutOfRangeException(nameof(destination));
        bool bag = destination == ItemDestination.Bag;
        var outcome = await this.SendAsync(bag ? "BAG" : "PC", (int)item, cancellationToken);
        int maximum = bag ? (item == PcItem.RareCandy ? 20 : 16) * 99 : 49950;
        if (outcome is { Status: "ok", Quantity: >= 99 } && outcome.Quantity <= maximum) return outcome.Quantity.Value;
        string storage = bag ? "Rucksackfach" : "Item-Lager";
        throw new InvalidOperationException(outcome.Status switch
        {
            "full" => $"Für 99 Stück ist im {storage} zu wenig Platz.",
            "storage" => $"Das {storage} dieses Spielstands konnte nicht sicher gelesen werden.",
            "location" => "Schließe das Spielmenü und kehre ins Spielfeld zurück.",
            _ => $"Spielstand oder Verbindung hat sich geändert. Prüfe das {storage}.",
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

    public async Task<(string Status, RawPokemon? Pokemon)> TradeAsync(TradeWork work, CancellationToken cancellationToken)
    {
        if (work.Outgoing.Length != 17 || work.Outgoing[8] != ':' ||
            !work.Outgoing.Remove(8, 1).All(Uri.IsHexDigit) ||
            !IsTradePayload(work.Prepared) || !IsTradePayload(work.Incoming))
            throw new InvalidDataException("Ungültige Tauschanweisung.");
        string action = work.Phase == PokemonTradePhase.Preparing ? "PREP" : "APPLY";
        var result = await this.SendAsync("TRADE", 0, cancellationToken, (request, epoch, trainer) =>
        {
            return FormattableString.Invariant(
                $"TRADE {request.Session} {epoch} {work.Trainer} {request.Id:D} {work.Id:N} {action} {work.Outgoing.Remove(8, 1)} {work.Prepared?.Hex ?? "-"} {work.Incoming?.Hex ?? "-"}\n");
        });
        return (result.Status, result.Pokemon);
    }

    private async Task<GrantResult> SendAsync(string action, int value, CancellationToken cancellationToken,
        Func<PendingGrant, string, uint, string>? command = null)
    {
        string epoch;
        uint trainer;
        PendingGrant request;
        lock (this.gate)
        {
            if (this.pending is not null) throw new InvalidOperationException("mGBA verarbeitet noch die vorherige Anfrage.");
            if (action is "LOAD" or "TRADE")
            {
                var target = this.loadBridge ?? throw new InvalidOperationException("mGBA ist noch nicht zum Laden bereit.");
                (epoch, trainer) = (target.Epoch, 0);
                request = new PendingGrant(target.Stream, target.Session, action, Guid.NewGuid(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            }
            else
            {
                var target = this.CurrentBridge() ?? throw new InvalidOperationException("Es sind keine aktuellen Spieldaten aus mGBA verfügbar.");
                if (action is "PC" or "BAG")
                {
                    if (target.InBattle) throw new InvalidOperationException("Verlasse den Kampf, bevor du Items hinzufügst.");
                    if (!target.HasParty) throw new InvalidOperationException("Hole zuerst dein erstes Pokémon ab.");
                }
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
            string packet = command?.Invoke(request, epoch, trainer) ??
                FormattableString.Invariant($"{action} {request.Session} {epoch} {trainer} {request.Id:D} {value} {expires}\n");
            await this.WriteAsync(request.Stream, packet, timeout.Token);
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

    private async Task WriteAsync(NetworkStream stream, string packet, CancellationToken cancellationToken)
    {
        await this.writeGate.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(packet), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled write may have delivered a partial line. Reconnect before sending any more commands.
            stream.Dispose();
            throw;
        }
        finally
        {
            this.writeGate.Release();
        }
    }

    private ReadyBridge? CurrentBridge() =>
        this.ready is { } bridge && DateTimeOffset.UtcNow - bridge.Seen < TimeSpan.FromSeconds(4) ? bridge : null;

    private static bool IsEpoch(string? epoch) =>
        epoch is { Length: > 0 and <= 48 } && epoch.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool IsTradePayload(RawPokemon? pokemon) =>
        pokemon is null || pokemon.Hex is { Length: 160 or 200 } && pokemon.Hex.All(Uri.IsHexDigit);

    private sealed record ReadyBridge(NetworkStream Stream, string Session, string Epoch, uint Trainer, bool InBattle, bool HasParty, DateTimeOffset Seen);
    private sealed record LoadBridge(NetworkStream Stream, string Session, string Epoch);

    private sealed record GrantResult(string Status, int? Quantity, RawPokemon? Pokemon);

    private sealed record PendingGrant(NetworkStream Stream, string Session, string Action, Guid Id, TaskCompletionSource<GrantResult> Completion);
}
