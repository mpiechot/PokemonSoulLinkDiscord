namespace SoulLink;

public enum PokemonTradePhase { Offered, Preparing, Committing, Completed, Declined, Cancelled }

public sealed record TradeOffer(Guid EncounterId, Guid OwnPokemon, Guid Partner, Guid PartnerPokemon);
public sealed record TradePlayer(Guid Id, string Name, PokemonState Pokemon);
public sealed record TradeView(Guid Id, string Link, PokemonTradePhase Phase, TradePlayer From, TradePlayer To, string? Reason = null);
public sealed record TradeWork(Guid Id, PokemonTradePhase Phase, uint Trainer, string Outgoing, RawPokemon? Prepared, RawPokemon? Incoming);

public sealed class PokemonTrade
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EncounterId { get; init; }
    public string Link { get; init; } = "";
    public DateTimeOffset Expires { get; set; }
    public PokemonTradePhase Phase { get; set; }
    public TradeSide From { get; init; } = new();
    public TradeSide To { get; init; } = new();
    public string? Reason { get; set; }

    public bool Includes(Guid player) => player == this.From.PlayerId || player == this.To.PlayerId;
    public bool Pending => this.Phase is PokemonTradePhase.Offered or PokemonTradePhase.Preparing or PokemonTradePhase.Committing;
}

public sealed class TradeSide
{
    public Guid PlayerId { get; init; }
    public Guid PokemonId { get; init; }
    public uint Trainer { get; init; }
    public PokemonState Outgoing { get; init; } = new();
    public RawPokemon? Prepared { get; set; }
    public PokemonState? Received { get; set; }
}
