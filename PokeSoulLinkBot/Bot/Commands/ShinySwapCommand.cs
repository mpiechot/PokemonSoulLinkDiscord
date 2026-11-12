using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Exchanges a shiny with its owner's living linked Pokémon.
/// </summary>
public sealed class ShinySwapCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShinySwapCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The response formatter.</param>
    public ShinySwapCommand(IRunService runService, EmbedFactory embedFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "shiny-swap";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Tauscht ein Shiny mit einem lebenden Link-Pokémon oder nimmt es zurück.")
            .AddOption("player", ApplicationCommandOptionType.User, "Der Besitzer des Shinys.", isRequired: true)
            .AddOption("shiny", ApplicationCommandOptionType.String, "Das Shiny auswählen oder seine ID eingeben.", isRequired: true, isAutocomplete: true)
            .AddOption("route", ApplicationCommandOptionType.String, "Zielroute; weglassen, um das ursprüngliche Pokémon zurückzuholen.", isRequired: false, isAutocomplete: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var player = CommandOptionHelper.GetRequiredUserOption(command, "player");
        var value = CommandOptionHelper.GetRequiredStringOption(command, "shiny");
        if (!Guid.TryParse(value, out var shinyId))
        {
            throw new InvalidOperationException("Bitte ein Shiny aus den Vorschlägen auswählen oder seine Shiny-ID eingeben.");
        }

        var route = command.Data.Options.FirstOrDefault(option => option.Name == "route")?.Value?.ToString();
        var run = this.runService.SwapShiny(guildId, player.Id, shinyId, route);
        var description = route is null
            ? $"Das ursprüngliche Pokémon von **{player.Username}** wurde zurückgeholt. Das Shiny ist wieder frei."
            : $"Das Shiny von **{player.Username}** ist jetzt auf **{route}** eingesetzt. Das ursprüngliche Pokémon bleibt aufbewahrt.";
        await response.SendAsync(embed: this.embedFactory.CreateActionEmbed("✨ Shiny getauscht", description));
        await response.SendFollowupsAsync(this.embedFactory.CreateStatusMessages(run));
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var playerValue = interaction.Data.Options.FirstOrDefault(option => option.Name == "player")?.Value;
        var playerId = playerValue is IUser user ? user.Id : ulong.TryParse(playerValue?.ToString(), out var id) ? id : 0;
        var run = this.runService.GetActiveRun(guildId);
        var input = AutocompleteHelper.GetCurrentValue(interaction);
        var results = interaction.Data.Current.Name == "shiny"
            ? CreateShinySuggestions(run, playerId, input)
            : AutocompleteHelper.CreateResults(
                run.LinkGroups.Where(group => !group.IsLostWithoutEncounter && group.Entries.Any(entry => entry.PlayerUserId == playerId && entry.IsAlive))
                    .Select(group => group.Route),
                input);
        await interaction.RespondAsync(results);
    }

    private static IReadOnlyCollection<AutocompleteResult> CreateShinySuggestions(SoulLinkRun run, ulong playerId, string input)
    {
        var suggestions = run.GetShinyAssignments()
            .Where(item => item.Pokemon.PlayerUserId == playerId && item.Pokemon.IsAlive)
            .Select(item => (
                $"{item.Pokemon.ShinyId!.Value.ToString()[..8]} · {item.Pokemon.PokemonName} · Fund: {item.Pokemon.ShinyCaughtRoute ?? "nicht angegeben"} · Einsatz: {item.Route ?? "frei"}",
                item.Pokemon.ShinyId!.Value.ToString()));
        return AutocompleteHelper.CreateResults(suggestions, input);
    }
}
