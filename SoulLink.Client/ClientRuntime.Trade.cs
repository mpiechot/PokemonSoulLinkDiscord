using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SoulLink;

namespace SoulLink.Client;

public sealed partial class ClientRuntime
{
    private DateTimeOffset nextTrade;
    private string? tradeHint;

    public string? TradeHint => Volatile.Read(ref this.tradeHint);

    public bool TradeUsesGame => this.CurrentTrade()?.Phase is PokemonTradePhase.Preparing or PokemonTradePhase.Committing;

    public async Task OfferTradeAsync(Guid runId, TradeOffer offer)
    {
        await this.connectionGate.WaitAsync();
        try
        {
            if (!this.OwnedBridgeReady) throw new InvalidOperationException("Öffne Rubin in SoulLink, um einen Tausch anzufragen.");
            var hub = this.RequireConnection(runId);
            await hub.InvokeAsync("OfferTrade", offer);
            this.UpdateRun(await hub.InvokeAsync<AutomationRun>("Status"));
        }
        finally { this.connectionGate.Release(); }
    }

    public async Task RespondToTradeAsync(Guid runId, Guid tradeId, bool accept)
    {
        await this.connectionGate.WaitAsync();
        try
        {
            var hub = this.RequireConnection(runId);
            await hub.InvokeAsync("RespondToTrade", tradeId, accept);
            this.UpdateRun(await hub.InvokeAsync<AutomationRun>("Status"));
        }
        finally { this.connectionGate.Release(); }
    }

    private TradeView? CurrentTrade()
    {
        lock (this.stateGate) return this.run?.Trade;
    }

    private async Task TickTradeAsync(HubConnection hub, DateTimeOffset now, CancellationToken token)
    {
        var trade = this.CurrentTrade();
        if (trade?.Phase is not (PokemonTradePhase.Preparing or PokemonTradePhase.Committing))
        {
            Volatile.Write(ref this.tradeHint, null);
            return;
        }
        if (now < this.nextTrade) return;
        this.nextTrade = now.AddSeconds(1);
        try
        {
            if (!this.OwnedBridgeReady)
            {
                Volatile.Write(ref this.tradeHint, "Öffne Rubin über „Run fortsetzen“, um den Tausch fortzusetzen.");
                return;
            }
            var work = await hub.InvokeAsync<TradeWork?>("TradeWork", trade.Id, token);
            if (work is null)
            {
                Volatile.Write(ref this.tradeHint, "Warte auf die Bestätigung des anderen Spielers.");
                return;
            }
            var (status, pokemon) = await this.items.TradeAsync(work, token);
            if (work.Phase == PokemonTradePhase.Preparing)
            {
                if (status == "ready" && pokemon is not null && work.Prepared is null)
                    await hub.InvokeAsync("PrepareTrade", trade.Id, pokemon, token);
                else if (status == "ineligible" && work.Prepared is null)
                    await hub.InvokeAsync("PrepareTrade", trade.Id, (RawPokemon?)null, token);
            }
            else if (status == "applied" && pokemon is not null)
                await hub.InvokeAsync("ConfirmTrade", trade.Id, pokemon, token);

            Volatile.Write(ref this.tradeHint, status switch
            {
                "ready" => "Warte, bis beide Pokémon tauschbereit sind.",
                "busy" => "Rubin tauscht die Pokémon und prüft die Entwicklung.",
                "applied" => "Spielstand gesichert. Warte auf die Bestätigung des anderen Spielers.",
                "field" => "Kehre für den Tausch ins Spielfeld zurück.",
                "storage" => "Der Tausch-Spielstand konnte nicht gesichert werden.",
                "conflict" => "Spielstand geändert. Starte Rubin über „Run fortsetzen“, um den Tausch fortzusetzen.",
                _ => "Warte auf die Bestätigung aus Rubin.",
            });
        }
        catch (Exception error) when (!token.IsCancellationRequested &&
            error is HubException or InvalidDataException or InvalidOperationException or IOException or SocketException or OperationCanceledException)
        {
            Volatile.Write(ref this.tradeHint, "Der Tausch wartet auf die Verbindung zu Rubin.");
        }
    }
}
