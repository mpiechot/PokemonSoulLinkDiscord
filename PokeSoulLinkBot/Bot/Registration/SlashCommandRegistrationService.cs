using Discord;
using Serilog;

namespace PokeSoulLinkBot.Bot.Registration;

/// <summary>
/// Registers slash commands only when local definitions differ from Discord's remote definitions.
/// </summary>
public sealed class SlashCommandRegistrationService
{
    /// <summary>
    /// Creates comparable snapshots from local command definitions.
    /// </summary>
    /// <param name="definitions">The local command definitions.</param>
    /// <returns>The local snapshots.</returns>
    public static IReadOnlyCollection<SlashCommandDefinitionSnapshot> CreateLocalSnapshots(
        IReadOnlyCollection<ApplicationCommandProperties> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions
            .Select(SlashCommandDefinitionSnapshot.FromDefinition)
            .OrderBy(snapshot => snapshot.Name, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Type)
            .ToList();
    }

    /// <summary>
    /// Compares local and remote command snapshots.
    /// </summary>
    /// <param name="localSnapshots">The local snapshots.</param>
    /// <param name="remoteSnapshots">The remote snapshots.</param>
    /// <returns><see langword="true"/> when the snapshots match; otherwise, <see langword="false"/>.</returns>
    public static bool DefinitionsMatch(
        IReadOnlyCollection<SlashCommandDefinitionSnapshot> localSnapshots,
        IReadOnlyCollection<SlashCommandDefinitionSnapshot> remoteSnapshots)
    {
        ArgumentNullException.ThrowIfNull(localSnapshots);
        ArgumentNullException.ThrowIfNull(remoteSnapshots);

        var orderedRemoteSnapshots = remoteSnapshots
            .OrderBy(snapshot => snapshot.Name, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.Type)
            .ToList();

        return localSnapshots.SequenceEqual(orderedRemoteSnapshots);
    }

    /// <summary>
    /// Registers slash commands for all provided targets.
    /// </summary>
    /// <param name="definitions">The local command definitions.</param>
    /// <param name="targets">The registration targets.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RegisterAsync(
        IReadOnlyCollection<ApplicationCommandProperties> definitions,
        IReadOnlyCollection<ISlashCommandRegistrationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            Log.Warning("Slash command registration has no configured targets.");
            return;
        }

        ApplicationCommandProperties[] commandDefinitions = definitions.ToArray();
        IReadOnlyCollection<SlashCommandDefinitionSnapshot> localSnapshots = CreateLocalSnapshots(commandDefinitions);

        foreach (ISlashCommandRegistrationTarget target in targets)
        {
            try
            {
                IReadOnlyCollection<SlashCommandDefinitionSnapshot> remoteSnapshots =
                    await target.GetRemoteCommandSnapshotsAsync();

                if (DefinitionsMatch(localSnapshots, remoteSnapshots))
                {
                    Log.Information("Slash commands are unchanged for {RegistrationTarget}. Skipping overwrite.", target.DisplayName);
                    continue;
                }

                Log.Information(
                    "Registering {CommandCount} slash commands for {RegistrationTarget}.",
                    commandDefinitions.Length,
                    target.DisplayName);
                await target.BulkOverwriteAsync(commandDefinitions);
                Log.Information(
                    "Registered {CommandCount} slash commands for {RegistrationTarget}.",
                    commandDefinitions.Length,
                    target.DisplayName);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Slash command registration failed for {RegistrationTarget}.", target.DisplayName);
            }
        }
    }
}
