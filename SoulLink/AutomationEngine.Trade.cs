namespace SoulLink;

public sealed partial class AutomationEngine
{
    public void OfferTrade(Registration registration, TradeOffer offer) => store.Execute(tx =>
    {
        var run = TradeRun(tx, registration);
        this.ExpireTrade(tx, run);
        if (!run.Selected || run.Phase != RunPhase.Active || run.Trade?.Pending == true || offer.Partner == registration.PlayerId)
            throw new InvalidOperationException("Ein Poké-Tausch benötigt einen laufenden Run und zwei verschiedene Spieler. Es darf nur eine Anfrage offen sein.");
        var encounter = run.Encounters.SingleOrDefault(link => link.Id == offer.EncounterId && !link.Dead && !link.Failed &&
            link.GroupId is not null && link.Members.GetValueOrDefault(registration.PlayerId) == offer.OwnPokemon &&
            link.Members.GetValueOrDefault(offer.Partner) == offer.PartnerPokemon)
            ?? throw new InvalidOperationException("Die Pokémon müssen zum selben lebenden Link gehören.");
        var from = this.TradeParticipant(run, registration.PlayerId, offer.OwnPokemon);
        var to = this.TradeParticipant(run, offer.Partner, offer.PartnerPokemon);
        if (from.Outgoing.Identity == to.Outgoing.Identity ||
            HasIdentity(run, from.PlayerId, to.Outgoing.Identity) || HasIdentity(run, to.PlayerId, from.Outgoing.Identity))
            throw new InvalidOperationException("Eine der Pokémon-Identitäten ist bereits im anderen Spielstand vorhanden.");
        run.Trade = new PokemonTrade
        {
            EncounterId = encounter.Id,
            Link = RubyNames.Area(encounter.Area),
            Expires = this.clock.GetUtcNow().AddMinutes(2),
            From = from,
            To = to,
        };
        tx.Save(run);
        return true;
    });

    public void RespondToTrade(Registration registration, Guid id, bool accept) => store.Execute(tx =>
    {
        var run = TradeRun(tx, registration);
        var trade = RequireTrade(run, registration, id);
        if (trade.To.PlayerId != registration.PlayerId || trade.Phase != PokemonTradePhase.Offered)
            throw new InvalidOperationException("Diese Tauschanfrage kann nicht mehr beantwortet werden.");
        if (this.clock.GetUtcNow() >= trade.Expires)
        {
            this.ExpireTrade(tx, run);
            return false;
        }
        trade.Phase = accept ? PokemonTradePhase.Preparing : PokemonTradePhase.Declined;
        trade.Expires = this.clock.GetUtcNow().AddMinutes(2);
        tx.Save(run);
        return true;
    });

    public TradeWork? WorkForTrade(Registration registration, Guid id) => store.Execute(tx =>
    {
        var run = TradeRun(tx, registration);
        this.ExpireTrade(tx, run);
        if (run.Trade is not { } trade || trade.Id != id || !trade.Includes(registration.PlayerId)) return null;
        var (own, other) = Sides(trade, registration);
        if (trade.Phase is not (PokemonTradePhase.Preparing or PokemonTradePhase.Committing) || own.Received is not null) return null;
        return new TradeWork(id, trade.Phase, own.Trainer, own.Outgoing.Identity, own.Prepared,
            trade.Phase == PokemonTradePhase.Committing ? other.Prepared : null);
    });

    public void PrepareTrade(Registration registration, Guid id, RawPokemon? raw) => store.Execute(tx =>
    {
        var run = TradeRun(tx, registration);
        var trade = RequireTrade(run, registration, id);
        this.ExpireTrade(tx, run);
        if (trade.Phase != PokemonTradePhase.Preparing) return false;
        var (own, _) = Sides(trade, registration);
        if (own.Prepared is not null) return false;
        try
        {
            if (raw is null) throw new InvalidDataException("Ein Pokémon ist nicht zum Tausch bereit. Pokémon mit Briefen können hier nicht getauscht werden.");
            var parsed = Gen3Parser.ParseTrade(raw);
            if (parsed.Identity != own.Outgoing.Identity || parsed.Species != own.Outgoing.Species)
                throw new InvalidDataException("Das ausgewählte Pokémon hat sich inzwischen geändert.");
            own.Prepared = raw with { LiveHp = null };
            if (trade.From.Prepared is not null && trade.To.Prepared is not null)
            {
                var link = run.Encounters.Single(e => e.Id == trade.EncounterId);
                if (!run.Selected || run.Phase != RunPhase.Active || link.Dead || link.Failed ||
                    link.Members.GetValueOrDefault(trade.From.PlayerId) != trade.From.PokemonId ||
                    link.Members.GetValueOrDefault(trade.To.PlayerId) != trade.To.PokemonId)
                    throw new InvalidDataException("Der Link ist inzwischen nicht mehr tauschbereit.");
                _ = this.TradeParticipant(run, trade.From.PlayerId, trade.From.PokemonId);
                _ = this.TradeParticipant(run, trade.To.PlayerId, trade.To.PokemonId);
                if (HasIdentity(run, trade.From.PlayerId, trade.To.Outgoing.Identity) ||
                    HasIdentity(run, trade.To.PlayerId, trade.From.Outgoing.Identity))
                    throw new InvalidDataException("Eine Pokémon-Identität ist inzwischen doppelt vorhanden.");
                trade.Phase = PokemonTradePhase.Committing;
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            CloseTrade(trade, PokemonTradePhase.Cancelled);
            trade.Reason = error.Message;
        }
        tx.Save(run);
        return true;
    });

    public void ConfirmTrade(Registration registration, Guid id, RawPokemon raw) => store.Execute(tx =>
    {
        var run = TradeRun(tx, registration);
        var trade = RequireTrade(run, registration, id);
        var (own, other) = Sides(trade, registration);
        if (trade.Phase != PokemonTradePhase.Committing || own.Received is not null) return false;
        var received = Gen3Parser.ParseTrade(raw);
        if (received.Identity != other.Outgoing.Identity || raw.Container != own.Prepared!.Container)
            throw new InvalidDataException("Die Bestätigung passt nicht zum vereinbarten Tausch.");
        own.Received = received;
        if (trade.From.Received is not null && trade.To.Received is not null)
        {
            var link = run.Encounters.Single(e => e.Id == trade.EncounterId);
            var from = run.Players.Single(p => p.Id == trade.From.PlayerId);
            var to = run.Players.Single(p => p.Id == trade.To.PlayerId);
            var fromPokemon = from.Pokemon.Single(p => p.Id == trade.From.PokemonId);
            var toPokemon = to.Pokemon.Single(p => p.Id == trade.To.PokemonId);
            from.Pokemon.Remove(fromPokemon);
            to.Pokemon.Remove(toPokemon);
            ReceivePokemon(from, toPokemon, trade.From);
            ReceivePokemon(to, fromPokemon, trade.To);
            link.Members[from.Id] = toPokemon.Id;
            link.Members[to.Id] = fromPokemon.Id;
            this.Event(tx, run, "trade", $"{link.Name}: {from.Name} und {to.Name} haben {RubyNames.Pokemon(trade.From.Outgoing.Species)} und {RubyNames.Pokemon(trade.To.Outgoing.Species)} getauscht.", notify: false);
            CloseTrade(trade, PokemonTradePhase.Completed);
        }
        tx.Save(run);
        return true;
    });

    public static TradeView? VisibleTrade(AutomationRun run, Guid playerId)
    {
        if (run.Trade is not { } trade || !trade.Includes(playerId)) return null;
        TradePlayer Project(TradeSide side) => new(side.PlayerId, run.Players.Single(p => p.Id == side.PlayerId).Name, side.Outgoing);
        return new TradeView(trade.Id, trade.Link, trade.Phase, Project(trade.From), Project(trade.To), trade.Reason);
    }

    private TradeSide TradeParticipant(AutomationRun run, Guid playerId, Guid monster)
    {
        var player = run.Players.SingleOrDefault(p => p.Id == playerId);
        var pokemon = player?.Pokemon.SingleOrDefault(p => p.Id == monster);
        if (player is null || player.TrainerId is not uint trainer || this.Connection(player) != "Ready" ||
            pokemon is not { Present: true, Dead: false, PendingDeath: null, State.IsEgg: false } || pokemon.State.Hp == 0)
            throw new InvalidOperationException("Beide Spieler und ihre ausgewählten Pokémon müssen im Spiel bereit sein.");
        return new TradeSide { PlayerId = playerId, PokemonId = monster, Outgoing = pokemon.State, Trainer = trainer };
    }

    private static bool HasIdentity(AutomationRun run, Guid player, string identity) =>
        run.Players.Single(p => p.Id == player).Pokemon.Any(p => p.State.Identity == identity);

    private static void ReceivePokemon(AutomationPlayer player, TrackedPokemon received, TradeSide own)
    {
        received.State = own.Received!;
        received.ZeroHpSamples = 0;
        received.FaintBattle = null;
        received.FaintDismissed = false;
        player.Pokemon.Add(received);
        player.TradedAway.Remove(received.State.Identity);
        player.TradedAway.Add(own.Outgoing.Identity);
    }

    private void ExpireTrade(AutomationStore.StoreTransaction tx, AutomationRun run)
    {
        if (run.Trade is { Phase: PokemonTradePhase.Preparing or PokemonTradePhase.Offered } trade && this.clock.GetUtcNow() >= trade.Expires)
        {
            CloseTrade(trade, PokemonTradePhase.Cancelled);
            trade.Reason = "Die Tauschanfrage ist abgelaufen.";
            tx.Save(run);
        }
    }

    private static void CloseTrade(PokemonTrade trade, PokemonTradePhase phase)
    {
        trade.Phase = phase;
        trade.From.Prepared = null;
        trade.To.Prepared = null;
    }

    private static void RequireNoCommittingTrade(AutomationRun run)
    {
        if (run.Trade?.Phase == PokemonTradePhase.Committing)
            throw new InvalidOperationException("Der Poké-Tausch muss zuerst in beiden Spielständen abgeschlossen werden.");
    }

    private static AutomationRun TradeRun(AutomationStore.StoreTransaction tx, Registration registration)
    {
        var run = tx.Run(registration.RunId);
        if (!run.Players.Any(p => p.Id == registration.PlayerId && p.ClientId == registration.ClientId))
            throw new UnauthorizedAccessException("Invalid player registration.");
        return run;
    }

    private static PokemonTrade RequireTrade(AutomationRun run, Registration registration, Guid id) =>
        run.Trade is { } trade && trade.Id == id && trade.Includes(registration.PlayerId)
            ? trade : throw new UnauthorizedAccessException("Keine passende Tauschanfrage gefunden.");

    private static (TradeSide Own, TradeSide Other) Sides(PokemonTrade trade, Registration registration) =>
        trade.From.PlayerId == registration.PlayerId ? (trade.From, trade.To) : (trade.To, trade.From);
}
