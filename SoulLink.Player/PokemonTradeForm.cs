using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class PokemonTradeForm : ToolWindow
{
    private readonly ClientRuntime runtime;
    private readonly RosterGrid grid;
    private readonly Button send = Action("Anfrage senden", DashboardTheme.Accent);
    private readonly Label hint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private ClientView? view;
    private ClientRunView? renderedRun;
    private Guid ownId;
    private Guid linkId;
    private Guid partnerId;
    private Guid partnerPokemon;
    private bool sending;
    private string? actionError;

    public PokemonTradeForm(ClientRuntime runtime, RubySprites sprites)
        : base("Poké-Tausch", new Size(1030, 640), resizable: true)
    {
        this.runtime = runtime;
        this.grid = new RosterGrid(sprites, null) { Dock = DockStyle.Fill };
        this.grid.PokemonOpened += this.SelectPokemon;
        this.send.Dock = DockStyle.Right;
        this.send.Width = 157;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(4, 5, 4, 3) };
        bottom.Controls.Add(this.hint);
        bottom.Controls.Add(this.send);
        this.Body.Controls.Add(this.grid);
        this.Body.Controls.Add(bottom);
        this.send.Click += async (_, _) => await this.SendAsync();
        this.Shown += (_, _) => this.FitWorkingArea();
        this.Render(runtime.Status());
    }

    public void Render(ClientView view)
    {
        this.view = view;
        if (!ReferenceEquals(this.renderedRun, view.Run))
        {
            if (this.renderedRun?.Id != view.Run?.Id) this.ClearSelection();
            this.renderedRun = view.Run;
            this.grid.Render(view, RosterView.Trade);
            if (this.ownId != Guid.Empty && this.SelectedLink() is null) this.ClearSelection();
        }
        this.UpdateSelection();
    }

    private void SelectPokemon(Guid player, PokemonState pokemon)
    {
        if (this.view?.LocalPlayerId is not Guid self || this.view.Run is not { } run || this.sending) return;
        this.actionError = null;
        var link = (run.Links ?? []).FirstOrDefault(row => row.Id != Guid.Empty && LivingLink(row) &&
            row.Members.Any(member => member.PlayerId == player && member.Pokemon.State.Identity == pokemon.Identity));
        var selected = link?.Members.FirstOrDefault(member => member.PlayerId == player && member.Pokemon.State.Identity == pokemon.Identity);
        if (selected?.Pokemon is not { Present: true, Dead: false, State.IsEgg: false } mon || mon.State.Hp == 0) return;
        if (player == self && link is not null && link.Members.Any(member => member.PlayerId != self && Eligible(member)))
        {
            if (this.ownId == mon.Id) this.ClearSelection();
            else
            {
                this.ClearSelection();
                this.ownId = mon.Id;
                this.linkId = link.Id;
            }
        }
        else if (this.ownId != Guid.Empty && link?.Id == this.linkId)
        {
            if (this.partnerPokemon == mon.Id) (this.partnerId, this.partnerPokemon) = (Guid.Empty, Guid.Empty);
            else (this.partnerId, this.partnerPokemon) = (player, mon.Id);
        }
        this.UpdateSelection();
    }

    private DashboardLink? SelectedLink() => this.view?.Run?.Links?.FirstOrDefault(row => row.Id == this.linkId &&
        LivingLink(row) && row.Members.Any(member => member.PlayerId == this.view.LocalPlayerId && member.Pokemon.Id == this.ownId && Eligible(member)));

    private static bool LivingLink(DashboardLink link) => link.State is "ACTIVE" or "BOX" or "TEAM CONFLICT";

    private static bool Eligible(DashboardLinkMember member) =>
        member.Pokemon is { Present: true, Dead: false, State.IsEgg: false } && member.Pokemon.State.Hp != 0;

    private void ClearSelection() => (this.linkId, this.ownId, this.partnerId, this.partnerPokemon) =
        (Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);

    private void UpdateSelection()
    {
        var run = this.view?.Run;
        var trade = run?.Trade;
        var link = this.SelectedLink();
        bool available = this.view?.IsLocalRun == false && run?.TradeAvailable == true && run.Phase == RunPhase.Active;
        bool pending = trade?.Phase is PokemonTradePhase.Offered or PokemonTradePhase.Preparing or PokemonTradePhase.Committing;
        bool target = link?.Members.Any(member => member.PlayerId == this.partnerId && member.Pokemon.Id == this.partnerPokemon && Eligible(member)) == true;
        this.grid.HighlightTrade(this.linkId, this.ownId, target ? this.partnerPokemon : Guid.Empty);
        this.send.Enabled = available && this.runtime.OwnedBridgeReady && !this.sending && !pending && target;
        this.hint.Text = this.actionError ?? trade?.Phase switch
        {
            PokemonTradePhase.Offered => "Die Tauschanfrage wartet auf eine Antwort.",
            PokemonTradePhase.Preparing or PokemonTradePhase.Committing => this.runtime.TradeHint ?? "Der Tausch wird vorbereitet.",
            _ when run?.TradeAvailable != true => "Der Poké-Tausch ist in einem verbundenen, aktualisierten Run verfügbar.",
            _ when run.Phase != RunPhase.Active => "Der Poké-Tausch ist verfügbar, sobald der Run gestartet ist.",
            _ when !this.runtime.OwnedBridgeReady => "Starte Rubin über SoulLink, um einen Tausch anzufragen.",
            _ when target => "Beide Pokémon sind ausgewählt.",
            _ when link is not null => "Wähle ein Pokémon eines Mitspielers in derselben Zeile.",
            PokemonTradePhase.Completed => "Der Tausch wurde in beiden Spielständen gespeichert.",
            PokemonTradePhase.Declined => "Die letzte Tauschanfrage wurde abgelehnt.",
            PokemonTradePhase.Cancelled when trade.Reason is not null => trade.Reason,
            _ => "Wähle ein eigenes lebendes Pokémon in einem Link.",
        };
    }

    private async Task SendAsync()
    {
        if (!this.send.Enabled || this.view?.Run is not { } run) return;
        this.sending = true;
        this.UpdateSelection();
        try
        {
            await this.runtime.OfferTradeAsync(run.Id, new TradeOffer(this.linkId, this.ownId, this.partnerId, this.partnerPokemon));
            this.ClearSelection();
        }
        catch (Exception error) when (RunDialogs.IsActionError(error))
        {
            this.actionError = "Die Anfrage konnte nicht gesendet werden. Prüfe den Link und ob beide Spieler verbunden sind.";
        }
        finally
        {
            this.sending = false;
            if (!this.IsDisposed) this.Render(this.runtime.Status());
        }
    }
}

internal sealed class PokemonTradeOfferForm : ToolWindow
{
    public PokemonTradeOfferForm(TradeView offer, RubySprites sprites, Func<bool, Task> respond)
        : base("Poké-Tauschanfrage", new Size(614, 340))
    {
        this.TradeId = offer.Id;
        var message = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"{offer.From.Name} möchte mit dir tauschen.",
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            AutoEllipsis = true,
        };
        var link = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"Link · {offer.Link}",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DashboardTheme.Muted,
        };
        var grid = new RosterGrid(sprites, null, showLocationTags: false)
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TabStop = false,
            AccessibleName = "Pokémon in der Tauschanfrage",
        };
        grid.Render(new RosterGridData(
            [new RosterColumn(offer.To.Id, "Du gibst"), new RosterColumn(offer.From.Id, "Du erhältst")],
            [new RosterRow(true, [new DashboardPokemon(offer.To.Pokemon, false, true), new DashboardPokemon(offer.From.Pokemon, false, true)], offer.Link)]));
        var accept = Action("Akzeptieren", DashboardTheme.Accent);
        var decline = Action("Ablehnen", DashboardTheme.Danger);
        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.AddRange([accept, decline]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(link, 0, 1);
        layout.Controls.Add(grid, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        this.Body.Controls.Add(layout);
        async Task AnswerAsync(bool answer)
        {
            accept.Enabled = decline.Enabled = false;
            try
            {
                await respond(answer);
                if (!this.IsDisposed) this.Close();
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                if (this.IsDisposed) return;
                message.Text = "Die Antwort konnte nicht übertragen werden. Versuche es erneut.";
                accept.Enabled = decline.Enabled = true;
            }
        }
        accept.Click += async (_, _) => await AnswerAsync(true);
        decline.Click += async (_, _) => await AnswerAsync(false);
    }

    public Guid TradeId { get; }
}
