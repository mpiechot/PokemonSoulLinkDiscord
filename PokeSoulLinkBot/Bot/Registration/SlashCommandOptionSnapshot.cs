using Discord;

namespace PokeSoulLinkBot.Bot.Registration;

/// <summary>
/// Represents the stable parts of a slash command option definition.
/// </summary>
public sealed class SlashCommandOptionSnapshot : IEquatable<SlashCommandOptionSnapshot>
{
    /// <summary>
    /// Gets or sets the option type.
    /// </summary>
    public ApplicationCommandOptionType Type { get; set; }

    /// <summary>
    /// Gets or sets the option name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the option description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the option is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the option has autocomplete enabled.
    /// </summary>
    public bool IsAutocomplete { get; set; }

    /// <summary>
    /// Gets or sets nested option snapshots.
    /// </summary>
    public List<SlashCommandOptionSnapshot> Options { get; set; } = new List<SlashCommandOptionSnapshot>();

    /// <summary>
    /// Creates a snapshot from local Discord.Net option properties.
    /// </summary>
    /// <param name="option">The local option definition.</param>
    /// <returns>The created snapshot.</returns>
    public static SlashCommandOptionSnapshot FromDefinition(ApplicationCommandOptionProperties option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new SlashCommandOptionSnapshot
        {
            Type = option.Type,
            Name = option.Name,
            Description = option.Description,
            IsRequired = option.IsRequired == true,
            IsAutocomplete = option.IsAutocomplete,
            Options = option.Options.Select(FromDefinition).ToList(),
        };
    }

    /// <summary>
    /// Creates a snapshot from a remote Discord command option.
    /// </summary>
    /// <param name="option">The remote option.</param>
    /// <returns>The created snapshot.</returns>
    public static SlashCommandOptionSnapshot FromRemote(IApplicationCommandOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new SlashCommandOptionSnapshot
        {
            Type = option.Type,
            Name = option.Name,
            Description = option.Description,
            IsRequired = option.IsRequired == true,
            IsAutocomplete = option.IsAutocomplete == true,
            Options = option.Options.Select(FromRemote).ToList(),
        };
    }

    /// <inheritdoc />
    public bool Equals(SlashCommandOptionSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.Type == other.Type
            && string.Equals(this.Name, other.Name, StringComparison.Ordinal)
            && string.Equals(this.Description, other.Description, StringComparison.Ordinal)
            && this.IsRequired == other.IsRequired
            && this.IsAutocomplete == other.IsAutocomplete
            && this.Options.SequenceEqual(other.Options);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SlashCommandOptionSnapshot other && this.Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = default(HashCode);
        hashCode.Add(this.Type);
        hashCode.Add(this.Name, StringComparer.Ordinal);
        hashCode.Add(this.Description, StringComparer.Ordinal);
        hashCode.Add(this.IsRequired);
        hashCode.Add(this.IsAutocomplete);

        foreach (SlashCommandOptionSnapshot option in this.Options)
        {
            hashCode.Add(option);
        }

        return hashCode.ToHashCode();
    }
}
