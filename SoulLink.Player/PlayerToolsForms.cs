using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class ItemMenuForm : PlayerWindow
{
    private static readonly (PcItem Item, string Name, string Sprite, Color Color)[] Items =
    [
        (PcItem.RareCandy, "Sonderbonbons", "rare-candy", DashboardTheme.Gold),
        (PcItem.PokeBall, "Pokébälle", "poke-ball", DashboardTheme.Danger),
        (PcItem.GreatBall, "Superbälle", "great-ball", Color.FromArgb(134, 194, 255)),
        (PcItem.UltraBall, "Hyperbälle", "ultra-ball", DashboardTheme.Gold),
        (PcItem.MasterBall, "Meisterbälle", "master-ball", Color.FromArgb(210, 163, 255)),
        (PcItem.SafariBall, "Safaribälle", "safari-ball", DashboardTheme.Accent),
        (PcItem.NetBall, "Netzbälle", "net-ball", DashboardTheme.Accent),
        (PcItem.DiveBall, "Tauchbälle", "dive-ball", Color.FromArgb(134, 194, 255)),
        (PcItem.NestBall, "Nestbälle", "nest-ball", DashboardTheme.Accent),
        (PcItem.RepeatBall, "Wiederbälle", "repeat-ball", DashboardTheme.Danger),
        (PcItem.TimerBall, "Timerbälle", "timer-ball", DashboardTheme.Muted),
        (PcItem.LuxuryBall, "Luxusbälle", "luxury-ball", DashboardTheme.Gold),
        (PcItem.PremierBall, "Premierbälle", "premier-ball", DashboardTheme.Text),
    ];

    private readonly RubySprites sprites;
    private readonly CancellationTokenSource closing = new();
    private readonly Button[] buttons = new Button[Items.Length];
    private readonly PictureBox[] images = new PictureBox[Items.Length];
    private readonly Label feedback = new() { Dock = DockStyle.Bottom, Height = 45, TextAlign = ContentAlignment.MiddleLeft };

    public ItemMenuForm(ClientRuntime runtime, RubySprites sprites)
    {
        RunDialogs.Configure(this, "Cheat-Menü", new Size(660, 570));
        this.sprites = sprites;
        var help = new Label
        {
            Text = "Ins PC-Item-Lager legen\nKehre im Spiel zuerst aufs Spielfeld zurück.",
            Dock = DockStyle.Top,
            Height = 52,
            ForeColor = DashboardTheme.Muted,
        };
        const int columns = 4;
        int rows = (Items.Length + columns - 1) / columns;
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows };
        for (int col = 0; col < columns; col++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        for (int row = 0; row < rows; row++) cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        for (int index = 0; index < Items.Length; index++)
        {
            var choice = Items[index];
            var card = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Fill,
                BackColor = DashboardTheme.Background,
                Margin = new Padding(4),
                Padding = new Padding(6),
            };
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            this.images[index] = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            var label = new Label { Text = choice.Name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            var button = DashboardTheme.Button("+99");
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.ForeColor = choice.Color;
            button.AccessibleName = $"99 {choice.Name} hinzufügen";
            button.Click += async (_, _) => await this.GrantAsync(runtime, choice.Item, choice.Name);
            this.buttons[index] = button;
            card.Controls.Add(this.images[index], 0, 0);
            card.Controls.Add(label, 0, 1);
            card.Controls.Add(button, 0, 2);
            cards.Controls.Add(card, index % columns, index / columns);
        }
        this.Controls.Add(cards);
        this.Controls.Add(this.feedback);
        this.Controls.Add(help);
        this.Shown += (_, _) => this.UpdateImages();
        this.sprites.Changed += this.UpdateImages;
    }

    private void UpdateImages()
    {
        for (int index = 0; index < Items.Length; index++)
            this.images[index].Image = this.sprites.FindItem(Items[index].Sprite);
    }

    private async Task GrantAsync(ClientRuntime runtime, PcItem item, string name)
    {
        foreach (var button in this.buttons) button.Enabled = false;
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = "Warte auf mGBA …";
        try
        {
            int total = await runtime.AddPcItemsAsync(item, this.closing.Token);
            if (this.IsDisposed) return;
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = $"+99 {name}. Jetzt insgesamt {total} im PC-Item-Lager.";
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is System.Net.Sockets.SocketException)
        {
            if (this.IsDisposed) return;
            this.feedback.ForeColor = DashboardTheme.Danger;
            this.feedback.Text = error.Message;
        }
        finally
        {
            if (!this.IsDisposed)
                foreach (var button in this.buttons) button.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.sprites.Changed -= this.UpdateImages;
            this.closing.Cancel();
            this.closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
