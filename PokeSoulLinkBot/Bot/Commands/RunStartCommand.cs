using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "run-start" slash command.
/// </summary>
public class RunStartCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStartCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="gameDataCatalogService">The game data catalog service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public RunStartCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    /// <inheritdoc />
    public string CommandName => "run-start";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName("run-start")
            .WithDescription("Start a new Soul Link run.")
            .AddOption("name", ApplicationCommandOptionType.String, "The run name.", isRequired: true)
            .AddOption("edition", ApplicationCommandOptionType.String, "The name of the played edition.", isRequired: true, isAutocomplete: true)
            .AddOption("player1", ApplicationCommandOptionType.User, "The first player.", isRequired: true)
            .AddOption("player2", ApplicationCommandOptionType.User, "The second player.", isRequired: true)
            .AddOption("player3", ApplicationCommandOptionType.User, "The third player.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var name = CommandOptionHelper.GetRequiredStringOption(command, "name");
        var edition = CommandOptionHelper.GetRequiredStringOption(command, "edition");
        var player1 = CommandOptionHelper.GetRequiredUserOption(command, "player1");
        var player2 = CommandOptionHelper.GetRequiredUserOption(command, "player2");
        var player3 = CommandOptionHelper.GetRequiredUserOption(command, "player3");

        IReadOnlyList<RunPlayer> players = CommandOptionHelper.CreatePlayers(player1, player2, player3);

        var run = this.runService.StartRun(guildId, name, edition, players);
        var image = this.embedImageFactory.CreateRunStartImage();
        var embed = this.embedFactory.CreateRunStartedEmbed(run, image.AttachmentUrl);

        await SlashCommandResponse.SendFileAsync(command, image.FileAttachment, embed: embed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var editions = await this.gameDataCatalogService.GetEditionsAsync();
        var results = AutocompleteHelper.CreateResults(
            editions.Select(edition => edition.DisplayName),
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug(
            "Run-start autocomplete returned {ResultCount} edition suggestions for value '{CurrentValue}'.",
            results.Count,
            AutocompleteHelper.GetCurrentValue(interaction));

        await interaction.RespondAsync(results);
    }
}
