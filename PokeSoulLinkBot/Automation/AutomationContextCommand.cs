using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Helpers;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Routes shared commands to the explicitly selected automated run.</summary>
public sealed class AutomationContextCommand : ISlashCommand
{
    private readonly ISlashCommand inner;
    private readonly AutomationEngine engine;

    /// <summary>Initializes the command context boundary.</summary>
    public AutomationContextCommand(ISlashCommand inner, AutomationEngine engine)
    {
        this.inner = inner;
        this.engine = engine;
    }

    /// <inheritdoc />
    public string CommandName => this.inner.CommandName;

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        if (this.CommandName != "run-start")
        {
            return this.inner.BuildDefinition();
        }

        return new SlashCommandBuilder()
            .WithName("run-start")
            .WithDescription("Start the selected automated lobby, or create a manual run with all options.")
            .AddOption("name", ApplicationCommandOptionType.String, "Manual run name.")
            .AddOption("edition", ApplicationCommandOptionType.String, "Manual run edition.", isAutocomplete: true)
            .AddOption("player1", ApplicationCommandOptionType.User, "Manual run first player.")
            .AddOption("player2", ApplicationCommandOptionType.User, "Manual run second player.")
            .AddOption("player3", ApplicationCommandOptionType.User, "Manual run third player.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        string guild = CommandOptionHelper.GetGuildId(command);
        var runs = this.engine.Runs(guild);
        var active = runs.SingleOrDefault(run => run.Selected);
        if (this.CommandName == "stats" && runs.Count > 0)
        {
            var lines = runs.Select(run => $"{run.Name} | {run.Phase} | {run.Players.Count} players | " +
                $"{run.Encounters.Count(e => e.GroupId != null)} links | {run.Encounters.Count(e => e.Dead)} dead links | " +
                $"{run.Players.Sum(p => p.Pokemon.Count)} observed Pokémon");
            await response.SendFollowupsAsync(StatusPresenter.Pages(lines));
            return;
        }

        if (active is null)
        {
            await this.inner.HandleAsync(command, response);
            return;
        }

        if (this.CommandName is "run-start" or "run-end")
        {
            if (command.User is not SocketGuildUser user || !user.GuildPermissions.ManageGuild)
            {
                throw new InvalidOperationException("Manage Server permission is required for automated run changes.");
            }

            active = this.engine.ChangeRun(guild, active.Id, this.CommandName == "run-start" ? "start" : "end");
            await response.SendAsync($"{active.Name}: {active.Phase}");
            return;
        }

        if (this.CommandName is "status" or "team")
        {
            await response.SendFollowupsAsync(StatusPresenter.Pages(StatusPresenter.Lines(active, this.engine.Connection)));
            return;
        }

        if (this.CommandName is "catch" or "catch-edit" or "catch-remove" or "death" or "death-undo" or
            "route-death" or "swap" or "use" or "arena-complete" or "team-check" or "catch-check")
        {
            throw new InvalidOperationException("An automated run is selected. Use /soullink for tracking and corrections; legacy manual commands cannot change its data.");
        }

        await this.inner.HandleAsync(command, response);
    }

    /// <inheritdoc />
    public Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        return this.inner.HandleAutocompleteAsync(interaction);
    }
}
