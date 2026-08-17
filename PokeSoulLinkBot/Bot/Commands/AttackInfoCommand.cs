using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class AttackInfoCommand : ISlashCommand
{
    private readonly IPokemonReferenceService referenceService;
    private readonly EmbedFactory embedFactory;

    public AttackInfoCommand(IPokemonReferenceService referenceService, EmbedFactory embedFactory)
    {
        this.referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    public string CommandName => "attack-info";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Zeigt Informationen zu einer Attacke.")
            .AddOption("move", ApplicationCommandOptionType.String, "Der englische API-Name, zum Beispiel thunderbolt.", isRequired: true)
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var move = CommandOptionHelper.GetRequiredStringOption(command, "move");
        var attackInfo = await this.referenceService.GetAttackInfoAsync(move)
            ?? throw new InvalidOperationException($"Die Attacke '{move}' wurde nicht gefunden.");
        await response.SendAsync(embed: this.embedFactory.CreateAttackInfoEmbed(attackInfo));
    }
}
