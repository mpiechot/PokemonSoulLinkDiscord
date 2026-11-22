namespace SoulLink.Player;

internal sealed class StatusEffectsForm : ToolWindow
{
    private static readonly Color Poison = Color.FromArgb(218, 168, 251);
    private static readonly Color Ice = Color.FromArgb(148, 216, 251);
    private static readonly Effect[] Conditions =
    [
        new("Verbrennung", "⅛ maximale KP pro Runde · physischer Grundschaden halbiert", DashboardTheme.Danger,
        [
            ("Schaden", "Am Rundenende verliert das Pokémon ⅛ seiner maximalen KP, auf ganze KP abgerundet und mindestens 1 KP. Im Gelände verursacht Verbrennung keinen Schritt-Schaden."),
            ("Angriff", "Die Schadensformel halbiert den physischen Grundschaden vor dem abschließenden Zuschlag von 2. Spezialattacken behalten ihren Schaden. In Rubin entscheidet der Typ der Attacke, ob sie physisch oder speziell ist."),
            ("Ausnahmen", "Adrenalin erhöht bei einem Hauptstatus den Angriff um 50 % und überspringt die Halbierung. Der KP-Verlust bleibt. Fassade verdoppelt bei Verbrennung, Paralyse oder Gift ihre Stärke von 70 auf 140. Ohne Adrenalin greift bei Fassade weiterhin die Halbierung."),
            ("Schutz und Fang", "Feuer-Pokémon und Aquahülle schützen vor Verbrennung. Bei einem wilden Ziel erhöht Verbrennung den Fangwert um den Faktor 1,5."),
        ]),
        new("Paralyse", "¼ der Initiative · 25 % Aussetzerchance", DashboardTheme.Gold,
        [
            ("Initiative", "Rubin teilt die für die Zugreihenfolge berechnete Initiative nach Stufen und anderen Modifikatoren durch 4. Die Priorität wird separat ausgewertet. Eine Attacke mit höherer Priorität kann weiterhin zuerst kommen."),
            ("Angriffsausfall", "Bei jedem erreichten Paralysecheck gibt es unabhängig davon eine Chance von 25 %, dass die gewählte Attacke ausfällt. Paralyse verursacht keinen KP-Verlust."),
            ("Schutz und Fang", "Flexibilität verhindert Paralyse. Elektro-Pokémon können in Rubin paralysiert werden. Die Boden-Immunität gegen Elektro-Attacken blockiert Donnerwelle. Bei einem wilden Ziel erhöht Paralyse den Fangwert um den Faktor 1,5."),
        ]),
        new("Vergiftung", "⅛ maximale KP pro Runde · auch im Gelände gefährlich", Poison,
        [
            ("Im Kampf", "Am Rundenende gehen ⅛ der maximalen KP verloren, abgerundet und mindestens 1 KP. Der Betrag bleibt jede Runde gleich."),
            ("Im Gelände", "Normales und schweres Gift ziehen betroffenen Teammitgliedern bei jedem vierten gezählten Feldschritt 1 KP ab. In Rubin kann das Pokémon dadurch auch außerhalb des Kampfes kampfunfähig werden."),
            ("Schutz und Fang", "Gift- und Stahl-Pokémon sowie die Fähigkeit Immunität schützen vor Vergiftung. Normales Gift erhöht den Fangwert eines wilden Ziels um den Faktor 1,5."),
        ]),
        new("Schwere Vergiftung", "Steigt jede Runde · 1 bis 15 Schadensstufen", Poison,
        [
            ("Schaden", "Rubin rundet zuerst maximale KP ÷ 16 ab, verwendet mindestens 1 KP und multipliziert diesen Betrag mit dem Giftzähler. Dieser beginnt bei 1 und steigt bei jedem Gifttick bis höchstens 15."),
            ("Wechsel und Gelände", "Schweres Gift bleibt beim Auswechseln und nach dem Kampf bestehen. Nach erneutem Einwechseln beginnt der Zähler wieder bei 1. Im Gelände gilt derselbe Verlust von 1 KP je vier gezählten Schritten wie bei normalem Gift."),
            ("Rubin-Besonderheit", "Die Fangformel von Rubin berücksichtigt normales Gift, aber überspringt schwere Vergiftung. Synchro kann schwere Vergiftung als normales Gift zurückgeben."),
        ]),
        new("Schlaf", "1 bis 4 blockierte Zugversuche bei gewöhnlichem Schlaf", Ice,
        [
            ("Dauer", "Gewöhnlicher Schlaf startet mit einem Zähler von 2 bis 5. Rubin zählt vor der Aktion herunter. Sobald der Zähler 0 erreicht, kann das Pokémon im selben Zug handeln."),
            ("Ausnahmen", "Schnarcher und Schlafrede können während des Schlafs benutzt werden. Frühwecker zählt doppelt so schnell herunter. Nach Erholung fallen normalerweise genau zwei eigene Zugversuche in den Schlaf."),
            ("Schutz und Fang", "Insomnia und Munterkeit verhindern Schlaf. Aufruhr kann Schlaf verhindern und Schlafende wecken. Schlaf verdoppelt den Fangwert."),
        ]),
        new("Einfrieren", "20 % Auftauchance beim Zugversuch", Ice,
        [
            ("Im Kampf", "Ohne Auftauen bleibt die gewählte Attacke blockiert. Der Auftaucheck hat pro Zugversuch eine Chance von 20 %. Nach dem Auftauen kann das Pokémon im selben Zug handeln."),
            ("Auftauen", "Flammenrad und Läuterfeuer können den eigenen Frost beim Einsatz aufheben. Bestimmte schädigende Feuerattacken tauen ein eingefrorenes Ziel auf."),
            ("Schutz und Fang", "Eis-Pokémon und Magmapanzer schützen vor Einfrieren. Bei wirksamer starker Sonne kann neuer Frost verhindert werden. Einfrieren verdoppelt den Fangwert."),
        ]),
        new("Weitere Kampfeffekte", "Vorübergehende Effekte und Wertestufen", DashboardTheme.Accent,
        [
            ("Verwirrung", "Bei einer noch aktiven Verwirrungsprüfung besteht eine 50-%-Chance auf einen Selbsttreffer statt der Aktion. Der Selbsttreffer wird als physischer Angriff mit Stärke 40 gegen die eigene Verteidigung berechnet. Die Verwirrung endet beim Auswechseln. Tempomacher schützt."),
            ("Anziehung", "Verliebtheit blockiert beim erreichten Check in 50 % der Fälle die Aktion. Sie endet, wenn das betroffene Pokémon oder der Auslöser ausgewechselt wird oder kampfunfähig wird. Dösigkeit schützt."),
            ("Zurückschrecken", "Die Attacke fällt für diese Runde aus, wenn das Pokémon beim Treffer noch nicht gehandelt hat. Konzentrator schützt."),
            ("Fesselattacken und Fluch", "Attacken wie Wickel oder Feuerwirbel hindern das Ziel für ihre Dauer am regulären Wechseln oder Fliehen und ziehen am Rundenende ¹⁄₁₆ der maximalen KP ab. Der Fluch eines Geist-Pokémon kostet das Ziel am Rundenende ¼ seiner maximalen KP und endet beim Auswechseln."),
            ("Egelsamen", "Am Rundenende verliert das Ziel ⅛ seiner maximalen KP. Diese KP gehen an das Pokémon auf dem Feldplatz des Anwenders. Pflanzen-Pokémon sind gegen Egelsamen geschützt."),
            ("Gähner, Nachtmahr und Abgesang", "Gähner führt am Ende der folgenden Runde zu Schlaf, solange das Ziel weiterhin betroffen ist und Schlaf möglich ist. Nachtmahr schadet einem schlafenden Ziel um ¼ seiner maximalen KP pro Runde. Abgesang macht betroffene Pokémon bei Zählerstand 0 kampfunfähig. Auswechseln entfernt diese drei Effekte."),
            ("Wertestufen", "Angriff, Verteidigung, Spezial-Angriff, Spezial-Verteidigung und Initiative reichen von −6 bis +6. Stufe +1 entspricht 1,5×, Stufe +2 entspricht 2× und Stufe −1 entspricht ⅔. Genauigkeit und Fluchtwert haben eigene Multiplikatoren. Die Stufen werden beim normalen Auswechseln zurückgesetzt."),
        ]),
    ];

    private readonly ToolTextStack document = new() { BackColor = DashboardTheme.Surface };
    private readonly DashboardScrollHost scroll;
    private readonly Button[] selectors;

    public StatusEffectsForm() : base("Status-Effekte · Pokémon Rubin", new Size(784, 550))
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 189));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 10, 0),
            AccessibleName = "Statuseffekte",
        };
        this.selectors = Conditions.Select((effect, index) =>
        {
            var button = Action(effect.Name, effect.Color);
            button.AutoSize = false;
            button.Size = new Size(175, 37);
            button.Margin = new Padding(0, 0, 0, 6);
            button.Click += (_, _) => this.SelectEffect(index);
            navigation.Controls.Add(button);
            return button;
        }).ToArray();

        var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0) };
        this.scroll = new DashboardScrollHost(this.document, this.document.Measure) { Dock = DockStyle.Fill, AccessibleName = "Effektdetails" };
        card.Controls.Add(this.scroll);
        layout.Controls.Add(navigation, 0, 0);
        layout.Controls.Add(card, 1, 0);
        this.Body.Controls.Add(layout);
        this.SelectEffect(0);
    }

    private void SelectEffect(int index)
    {
        for (int i = 0; i < this.selectors.Length; i++)
            this.selectors[i].BackColor = i == index ? DashboardTheme.Border : DashboardTheme.Panel;
        var effect = Conditions[index];
        var title = Heading(effect.Name);
        title.ForeColor = effect.Color;
        var summary = new Label { Text = effect.Summary, ForeColor = DashboardTheme.Muted, Margin = new Padding(0, 1, 5, 14) };
        this.document.SetRows(new Control[] { title, summary }.Concat(effect.Paragraphs.SelectMany(part => new Control[]
        {
            Heading(part.Title),
            new Label { Text = part.Text, Margin = new Padding(0, 2, 5, 10) },
        })));
        this.scroll.ResizeDocument();
        this.scroll.ScrollToTop();
    }

    private sealed record Effect(string Name, string Summary, Color Color, (string Title, string Text)[] Paragraphs);
}
