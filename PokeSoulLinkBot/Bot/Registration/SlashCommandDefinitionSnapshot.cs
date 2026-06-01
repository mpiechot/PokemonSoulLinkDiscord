using Discord;

namespace PokeSoulLinkBot.Bot.Registration;

/// <summary>
/// Represents the stable parts of a slash command definition.
/// </summary>
public sealed class SlashCommandDefinitionSnapshot : IEquatable<SlashCommandDefinitionSnapshot>
{
    /// <summary>
    /// Gets or sets the command type.
    /// </summary>
    public ApplicationCommandType Type { get; set; } = ApplicationCommandType.Slash;

    /// <summary>
    /// Gets or sets the command name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets option snapshots.
    /// </summary>
    public List<SlashCommandOptionSnapshot> Options { get; set; } = new List<SlashCommandOptionSnapshot>();

    /// <summary>
    /// Creates a snapshot from local Discord.Net command properties.
    /// </summary>
    /// <param name="definition">The local command definition.</param>
    /// <returns>The created snapshot.</returns>
    public static SlashCommandDefinitionSnapshot FromDefinition(ApplicationCommandProperties definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var slashDefinition = definition as SlashCommandProperties
            ?? throw new InvalidOperationException("Only slash command definitions can be registered.");

        return new SlashCommandDefinitionSnapshot
        {
            Type = ApplicationCommandType.Slash,
            Name = slashDefinition.Name.Value,
            Description = slashDefinition.Description.Value,
            Options = slashDefinition.Options.IsSpecified
                ? slashDefinition.Options.Value.Select(SlashCommandOptionSnapshot.FromDefinition).ToList()
                : new List<SlashCommandOptionSnapshot>(),
        };
    }

    /// <summary>
    /// Creates a snapshot from a remote Discord command.
    /// </summary>
    /// <param name="command">The remote command.</param>
    /// <returns>The created snapshot.</returns>
    public static SlashCommandDefinitionSnapshot FromRemote(IApplicationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new SlashCommandDefinitionSnapshot
        {
            Type = command.Type,
            Name = command.Name,
            Description = command.Description,
            Options = command.Options.Select(SlashCommandOptionSnapshot.FromRemote).ToList(),
        };
    }

    /// <inheritdoc />
    public bool Equals(SlashCommandDefinitionSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.Type == other.Type
            && string.Equals(this.Name, other.Name, StringComparison.Ordinal)
            && string.Equals(this.Description, other.Description, StringComparison.Ordinal)
            && this.Options.SequenceEqual(other.Options);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SlashCommandDefinitionSnapshot other && this.Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = default(HashCode);
        hashCode.Add(this.Type);
        hashCode.Add(this.Name, StringComparer.Ordinal);
        hashCode.Add(this.Description, StringComparer.Ordinal);

        foreach (SlashCommandOptionSnapshot option in this.Options)
        {
            hashCode.Add(option);
        }

        return hashCode.ToHashCode();
    }
}
