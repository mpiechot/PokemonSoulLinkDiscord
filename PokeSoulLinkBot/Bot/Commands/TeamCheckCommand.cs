using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class TeamCheckCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly TeamCheckAnalyzer analyzer;

    public TeamCheckCommand(IRunService runService, EmbedFactory embedFactory, TeamCheckAnalyzer analyzer)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public string CommandName => "team-check";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Prüft die Typabdeckung und empfiehlt ein optimales Team.")
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var guildId = CommandOptionHelper.GetGuildId(command);
        var run = this.runService.GetActiveRun(guildId);
        var analysis = this.analyzer.Analyze(run);
        await response.SendAsync(embed: this.embedFactory.CreateTeamCheckEmbed(run, analysis));

        var recommendedRun = new SoulLinkRun
        {
            Name = run.Name,
            Game = run.Game,
            Players = run.Players,
            ActiveLinks = analysis.OptimalLinkGroups.Take(6).ToArray(),
        };
        var teamMessages = this.embedFactory.CreateTeamMessages(recommendedRun, "Empfohlenes Team");
        await response.SendFollowupsAsync(teamMessages);
    }
}
