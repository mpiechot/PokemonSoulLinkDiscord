using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class TypeCommand : ISlashCommand
{
    private readonly IPokemonReferenceService referenceService;
    private readonly EmbedFactory embedFactory;

    public TypeCommand(IPokemonReferenceService referenceService, EmbedFactory embedFactory)
    {
        this.referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    public string CommandName => "type";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Zeigt die Effektivität eines Pokémon-Typs.")
            .AddOption(
                new SlashCommandOptionBuilder()
                    .WithName("type")
                    .WithDescription("Der Pokémon-Typ.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddChoice("Normal", "normal")
                    .AddChoice("Feuer", "fire")
                    .AddChoice("Wasser", "water")
                    .AddChoice("Pflanze", "grass")
                    .AddChoice("Elektro", "electric")
                    .AddChoice("Eis", "ice")
                    .AddChoice("Kampf", "fighting")
                    .AddChoice("Gift", "poison")
                    .AddChoice("Boden", "ground")
                    .AddChoice("Flug", "flying")
                    .AddChoice("Psycho", "psychic")
                    .AddChoice("Käfer", "bug")
                    .AddChoice("Gestein", "rock")
                    .AddChoice("Geist", "ghost")
                    .AddChoice("Drache", "dragon")
                    .AddChoice("Unlicht", "dark")
                    .AddChoice("Stahl", "steel")
                    .AddChoice("Fee", "fairy"))
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var type = CommandOptionHelper.GetRequiredStringOption(command, "type");
        var typeInfo = await this.referenceService.GetTypeInfoAsync(type)
            ?? throw new InvalidOperationException($"Der Typ '{type}' wurde nicht gefunden.");
        await response.SendAsync(embed: this.embedFactory.CreateTypeInfoEmbed(typeInfo));
    }
}
