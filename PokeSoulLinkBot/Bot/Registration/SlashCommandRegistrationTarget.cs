using Discord;

namespace PokeSoulLinkBot.Bot.Registration;

/// <summary>
/// Provides a delegate-backed slash command registration target.
/// </summary>
public sealed class SlashCommandRegistrationTarget : ISlashCommandRegistrationTarget
{
    private readonly Func<Task<IReadOnlyCollection<IApplicationCommand>>> getRemoteCommandsAsync;
    private readonly Func<ApplicationCommandProperties[], Task> bulkOverwriteAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlashCommandRegistrationTarget"/> class.
    /// </summary>
    /// <param name="displayName">The display name used in logs.</param>
    /// <param name="getRemoteCommandsAsync">The remote command loader.</param>
    /// <param name="bulkOverwriteAsync">The bulk overwrite operation.</param>
    public SlashCommandRegistrationTarget(
        string displayName,
        Func<Task<IReadOnlyCollection<IApplicationCommand>>> getRemoteCommandsAsync,
        Func<ApplicationCommandProperties[], Task> bulkOverwriteAsync)
    {
        this.DisplayName = displayName;
        this.getRemoteCommandsAsync = getRemoteCommandsAsync ?? throw new ArgumentNullException(nameof(getRemoteCommandsAsync));
        this.bulkOverwriteAsync = bulkOverwriteAsync ?? throw new ArgumentNullException(nameof(bulkOverwriteAsync));
    }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SlashCommandDefinitionSnapshot>> GetRemoteCommandSnapshotsAsync()
    {
        IReadOnlyCollection<IApplicationCommand> commands = await this.getRemoteCommandsAsync();
        return commands
            .Where(command => command.Type == ApplicationCommandType.Slash)
            .Select(SlashCommandDefinitionSnapshot.FromRemote)
            .ToList();
    }

    /// <inheritdoc />
    public Task BulkOverwriteAsync(ApplicationCommandProperties[] definitions)
    {
        return this.bulkOverwriteAsync(definitions);
    }
}
