using Discord.WebSocket;
using PokeSoulLinkBot.Core.Models;
using System.Numerics;

namespace PokeSoulLinkBot.Bot.Helpers;

/// <summary>
/// Provides helper methods for extracting and mapping slash command options.
/// </summary>
public static class CommandOptionHelper
{
    /// <summary>
    /// Gets the guild identifier from the command.
    /// </summary>
    /// <param name="command">The slash command.</param>
    /// <returns>The guild identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the command is not executed within a guild.
    /// </exception>
    public static string GetGuildId(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.GuildId?.ToString()
            ?? throw new InvalidOperationException("This command can only be used inside a guild.");
    }

    /// <summary>
    /// Gets a required string option value.
    /// </summary>
    /// <param name="command">The slash command.</param>
    /// <param name="optionName">The option name.</param>
    /// <returns>The string value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the option is missing or has no value.
    /// </exception>
    public static string GetRequiredStringOption(SocketSlashCommand command, string optionName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        var option = command.Data.Options.FirstOrDefault(x => x.Name == optionName)
            ?? throw new InvalidOperationException($"The option '{optionName}' is missing.");

        var value = option.Value?.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The option '{optionName}' is missing.");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional string option value.
    /// </summary>
    /// <param name="command">The slash command.</param>
    /// <param name="optionName">The option name.</param>
    /// <returns>The string value if present; otherwise, <see langword="null"/>.</returns>
    public static string? GetOptionalStringOption(SocketSlashCommand command, string optionName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        return command.Data.Options
            .FirstOrDefault(x => x.Name == optionName)
            ?.Value?
            .ToString();
    }

    public static int GetRequiredIntegerOption(SocketSlashCommand command, string optionName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        var option = command.Data.Options.FirstOrDefault(x => x.Name == optionName);

        if (option == null)
        {
            throw new InvalidOperationException("Invalid command option.");
        }

        return Convert.ToInt32(option.Value);
    }

    /// <summary>
    /// Gets a required user option value.
    /// </summary>
    /// <param name="command">The slash command.</param>
    /// <param name="optionName">The option name.</param>
    /// <returns>The selected guild user.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the option is missing or invalid.
    /// </exception>
    public static SocketGuildUser GetRequiredUserOption(SocketSlashCommand command, string optionName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        var option = command.Data.Options.FirstOrDefault(x => x.Name == optionName)
            ?? throw new InvalidOperationException($"The option '{optionName}' is missing.");

        if (option.Value is not SocketGuildUser user)
        {
            throw new InvalidOperationException($"The option '{optionName}' is not a valid guild user.");
        }

        return user;
    }

    /// <summary>
    /// Creates run players from Discord guild users.
    /// </summary>
    /// <param name="users">The guild users.</param>
    /// <returns>A read-only list of run players.</returns>
    public static IReadOnlyList<RunPlayer> CreatePlayers(params SocketGuildUser[] users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return users
            .Select(user => new RunPlayer
            {
                UserId = user.Id,
                UserName = user.Username,
            })
            .ToList();
    }
}