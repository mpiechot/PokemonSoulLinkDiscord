using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Helpers;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Guild-scoped administration and status for automated runs.</summary>
public sealed class AutomationCommand : ISlashCommand
{
    private readonly string name;
    private readonly AutomationEngine engine;

    /// <summary>Initializes a command bound to the shared automation service.</summary>
    public AutomationCommand(string name, AutomationEngine engine)
    {
        this.name = name;
        this.engine = engine;
    }

    /// <summary>Gets the automation commands that coexist with legacy manual runs.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "run-init", "show-runs", "run-load", "soullink" };

    /// <inheritdoc />
    public string CommandName => this.name;

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        var builder = new SlashCommandBuilder().WithName(this.name).WithDescription("Manage and inspect automatic SoulLink tracking.")
            .WithContextTypes(InteractionContextType.Guild);
        if (this.name != "show-runs")
        {
            builder.WithDefaultMemberPermissions(GuildPermission.ManageGuild);
        }

        if (this.name == "run-init")
        {
            builder.AddOption("name", ApplicationCommandOptionType.String, "Run name.", isRequired: true)
                .AddOption("edition", ApplicationCommandOptionType.String, "Edition (currently ruby / AXVD).")
                .AddOption("death-rule", ApplicationCommandOptionType.String, "confirm-faint (default), disabled or zero-hp-outside-battle.");
        }
        else if (this.name == "run-load")
        {
            builder.AddOption("id", ApplicationCommandOptionType.String, "Run ID from show-runs.", isRequired: true);
        }
        else if (this.name == "soullink")
        {
            builder.AddOption("action", ApplicationCommandOptionType.String, "status, start, pause, end, rebaseline, encounter-failed, death or death-undo.", isRequired: true)
                .AddOption("player", ApplicationCommandOptionType.String, "Player ID for explicit rebaseline.")
                .AddOption("area", ApplicationCommandOptionType.Integer, "Region section ID for encounter-failed.");
        }

        return builder.Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        string guild = CommandOptionHelper.GetGuildId(command);
        string action = command.Data.Options.FirstOrDefault(o => o.Name == "action")?.Value?.ToString() ?? "status";
        bool mutation = this.name is "run-init" or "run-load" || (this.name == "soullink" && action != "status");
        if (mutation && (command.User is not SocketGuildUser user || !user.GuildPermissions.ManageGuild))
        {
            throw new InvalidOperationException("Manage Server permission is required.");
        }

        if (this.name == "run-init")
        {
            string edition = command.Data.Options.FirstOrDefault(o => o.Name == "edition")?.Value?.ToString() ?? "ruby";
            if (edition != "ruby" && edition != "rubin" && edition != "AXVD")
            {
                throw new InvalidDataException("Only Ruby AXVD is currently supported.");
            }

            string rule = command.Data.Options.FirstOrDefault(o => o.Name == "death-rule")?.Value?.ToString() ?? "confirm-faint";
            var deathRule = rule switch
            {
                "confirm-faint" => DeathRule.ConfirmFaint,
                "disabled" => DeathRule.Disabled,
                "zero-hp-outside-battle" => DeathRule.ZeroHpOutsideBattle,
                _ => throw new InvalidDataException("Unknown death rule."),
            };

            var run = this.engine.Initialize(
                guild,
                command.Channel.Id,
                CommandOptionHelper.GetRequiredStringOption(command, "name"),
                deathRule);
            await response.SendAsync($"Lobby **{run.Name}** created. Invitation: `{run.Invitation}`\nJoin with the local client, then `/soullink action:start`. Existing manual runs remain available.", ephemeral: true);
            return;
        }

        var runs = this.engine.Runs(guild);
        if (this.name == "show-runs")
        {
            await response.SendFollowupsAsync(StatusPresenter.Pages(runs.Select(r => $"{r.Name} | {r.Id} | {r.Edition} | {r.Phase} | {string.Join(", ", r.Players.Select(p => p.Name))}")));
            if (runs.Count == 0)
            {
                await response.SendAsync("No automated runs yet. Use /run-init.");
            }

            return;
        }

        if (this.name == "run-load")
        {
            var loaded = this.engine.ChangeRun(guild, Guid.Parse(CommandOptionHelper.GetRequiredStringOption(command, "id")), "load");
            await response.SendAsync($"Loaded {loaded.Name}. Reconnect clients to submit fresh snapshots.");
            return;
        }

        var active = runs.SingleOrDefault(r => r.Selected) ?? throw new InvalidOperationException("Use /run-init or /run-load first.");
        if (action == "rebaseline")
        {
            this.engine.Rebaseline(guild, active.Id, Guid.Parse(CommandOptionHelper.GetRequiredStringOption(command, "player")));
            await response.SendAsync("Rebaseline authorized. Reconnect that client; existing Pokémon are imported without catch replay.");
            return;
        }

        if (action == "encounter-failed")
        {
            int area = Convert.ToInt32(command.Data.Options.Single(o => o.Name == "area").Value);
            this.engine.SetEncounterFailed(guild, active.Id, area);
        }
        else if (action is "death" or "death-undo")
        {
            int area = Convert.ToInt32(command.Data.Options.Single(o => o.Name == "area").Value);
            this.engine.SetDeath(guild, active.Id, area, action == "death");
        }
        else if (action != "status")
        {
            active = this.engine.ChangeRun(guild, active.Id, action);
        }

        foreach (string page in StatusPresenter.Pages(StatusPresenter.Lines(active, this.engine.Connection)))
        {
            await response.SendAsync(page);
        }
    }
}
