using Discord;
using PokeSoulLinkBot.Bot.Registration;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class SlashCommandRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_ShouldSkipOverwriteWhenDefinitionsAreUnchanged()
    {
        ApplicationCommandProperties[] definitions = CreateDefinitions("Shows the current run status.");
        IReadOnlyCollection<SlashCommandDefinitionSnapshot> remoteSnapshots =
            SlashCommandRegistrationService.CreateLocalSnapshots(definitions);
        var target = new FakeRegistrationTarget(remoteSnapshots);
        var service = new SlashCommandRegistrationService();

        await service.RegisterAsync(definitions, new[] { target });

        Assert.Equal(0, target.BulkOverwriteCount);
    }

    [Fact]
    public async Task RegisterAsync_ShouldOverwriteWhenDefinitionsChanged()
    {
        ApplicationCommandProperties[] definitions = CreateDefinitions("Shows the current run status.");
        var remoteSnapshots = new[]
        {
            new SlashCommandDefinitionSnapshot
            {
                Type = ApplicationCommandType.Slash,
                Name = "status",
                Description = "Old description.",
            },
        };
        var target = new FakeRegistrationTarget(remoteSnapshots);
        var service = new SlashCommandRegistrationService();

        await service.RegisterAsync(definitions, new[] { target });

        Assert.Equal(1, target.BulkOverwriteCount);
        Assert.NotNull(target.LastDefinitions);
        Assert.Equal(definitions.Select(definition => definition.Name.Value), target.LastDefinitions.Select(definition => definition.Name.Value));
    }

    [Fact]
    public async Task RegisterAsync_ShouldContinueWhenOneTargetFails()
    {
        ApplicationCommandProperties[] definitions = CreateDefinitions("Shows the current run status.");
        var failingTarget = new FakeRegistrationTarget(Array.Empty<SlashCommandDefinitionSnapshot>())
        {
            ThrowOnRead = true,
        };
        var changedTarget = new FakeRegistrationTarget(Array.Empty<SlashCommandDefinitionSnapshot>());
        var service = new SlashCommandRegistrationService();

        await service.RegisterAsync(definitions, new[] { failingTarget, changedTarget });

        Assert.Equal(0, failingTarget.BulkOverwriteCount);
        Assert.Equal(1, changedTarget.BulkOverwriteCount);
    }

    private static ApplicationCommandProperties[] CreateDefinitions(string description)
    {
        return new[]
        {
            new SlashCommandBuilder()
                .WithName("status")
                .WithDescription(description)
                .AddOption("verbose", ApplicationCommandOptionType.Boolean, "Show more details.", isRequired: false)
                .Build(),
        };
    }

    private sealed class FakeRegistrationTarget : ISlashCommandRegistrationTarget
    {
        private readonly IReadOnlyCollection<SlashCommandDefinitionSnapshot> remoteSnapshots;

        public FakeRegistrationTarget(IReadOnlyCollection<SlashCommandDefinitionSnapshot> remoteSnapshots)
        {
            this.remoteSnapshots = remoteSnapshots;
        }

        public string DisplayName => "fake target";

        public int BulkOverwriteCount { get; private set; }

        public ApplicationCommandProperties[]? LastDefinitions { get; private set; }

        public bool ThrowOnRead { get; set; }

        public Task<IReadOnlyCollection<SlashCommandDefinitionSnapshot>> GetRemoteCommandSnapshotsAsync()
        {
            if (this.ThrowOnRead)
            {
                throw new InvalidOperationException("Discord is temporarily unavailable.");
            }

            return Task.FromResult(this.remoteSnapshots);
        }

        public Task BulkOverwriteAsync(ApplicationCommandProperties[] definitions)
        {
            this.BulkOverwriteCount++;
            this.LastDefinitions = definitions;
            return Task.CompletedTask;
        }
    }
}
