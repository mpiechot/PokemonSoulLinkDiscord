using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "health" slash command.
/// </summary>
public sealed class HealthCommand : ISlashCommand
{
    private readonly IBotHealthService healthService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCommand"/> class.
    /// </summary>
    /// <param name="healthService">The health service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    public HealthCommand(
        IBotHealthService healthService,
        EmbedFactory embedFactory)
    {
        this.healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "health";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Show bot health and recent diagnostics.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var report = await this.healthService.GetReportAsync(guildId);
        var embed = this.embedFactory.CreateHealthEmbed(report);

        await response.SendAsync(embed: embed);
    }
}
