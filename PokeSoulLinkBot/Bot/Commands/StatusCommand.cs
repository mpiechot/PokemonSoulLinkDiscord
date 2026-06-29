using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "run-start" slash command.
/// </summary>
public class StatusCommand : ISlashCommand
{
    private const int MaxParallelPokemonTypeLookups = 4;

    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IPokemonLookupService pokemonLookupService;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="pokemonLookupService">The Pokémon lookup service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public StatusCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IPokemonLookupService pokemonLookupService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.pokemonLookupService = pokemonLookupService ?? throw new ArgumentNullException(nameof(pokemonLookupService));
    }

    /// <inheritdoc />
    public string CommandName => "status";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName("status")
            .WithDescription("Show the current run status.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var activeRun = this.runService.GetActiveRun(guildId);
        await this.EnrichMissingPokemonTypesAsync(activeRun);

        var messages = this.embedFactory.CreateStatusMessages(activeRun);
        var image = this.embedImageFactory.CreateStatusImage();
        var embed = this.embedFactory.CreateRunSummaryEmbed("Run Status", activeRun, image.AttachmentUrl);
        await response.SendFileAsync(image.FileAttachment, text: messages[0], embed: embed);

        await response.SendFollowupsAsync(messages.Skip(1));
    }

    private async Task EnrichMissingPokemonTypesAsync(SoulLinkRun run)
    {
        var entriesWithoutTypes = run.LinkGroups
            .SelectMany(group => group.Entries)
            .Where(entry => entry.Types.Count == 0)
            .ToList();

        if (entriesWithoutTypes.Count == 0)
        {
            return;
        }

        var typesByPokemonName = new ConcurrentDictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        using var lookupLimiter = new SemaphoreSlim(MaxParallelPokemonTypeLookups, MaxParallelPokemonTypeLookups);
        var lookupTasks = entriesWithoutTypes
            .Select(entry => entry.PokemonName)
            .Where(pokemonName => !string.IsNullOrWhiteSpace(pokemonName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(pokemonName => this.LookupPokemonTypesAsync(pokemonName, typesByPokemonName, lookupLimiter));

        await Task.WhenAll(lookupTasks);

        foreach (var entry in entriesWithoutTypes)
        {
            if (typesByPokemonName.TryGetValue(entry.PokemonName, out var pokemonTypes))
            {
                entry.Types = pokemonTypes.ToList();
            }
        }
    }

    private async Task LookupPokemonTypesAsync(
        string pokemonName,
        ConcurrentDictionary<string, IReadOnlyList<string>> typesByPokemonName,
        SemaphoreSlim lookupLimiter)
    {
        await lookupLimiter.WaitAsync();
        try
        {
            var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(pokemonName);
            if (pokemonInfo?.Types.Count > 0)
            {
                typesByPokemonName.TryAdd(pokemonName, pokemonInfo.Types);
            }
        }
        finally
        {
            lookupLimiter.Release();
        }
    }
}
