using System.Text.Json;
using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;

namespace PokeSoulLinkBot.Bot.Handlers;

/// <summary>
/// Routes slash commands to their dedicated command implementations.
/// </summary>
public sealed class SlashCommandRouter
{
    private readonly IReadOnlyDictionary<string, ISlashCommand> commands;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlashCommandRouter"/> class.
    /// </summary>
    /// <param name="commands">The available slash commands.</param>
    /// <param name="embedFactory">The embed factory used for error messages.</param>
    public SlashCommandRouter(
        IReadOnlyCollection<ISlashCommand> commands,
        EmbedFactory embedFactory)
    {
        ArgumentNullException.ThrowIfNull(commands);

        this.commands = commands.ToDictionary(command => command.CommandName, StringComparer.OrdinalIgnoreCase);
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <summary>
    /// Gets all slash command definitions.
    /// </summary>
    /// <returns>A read-only collection of command definitions.</returns>
    public IReadOnlyCollection<ApplicationCommandProperties> GetDefinitions()
    {
        return this.commands.Values
            .Select(command => command.BuildDefinition())
            .ToList();
    }

    /// <summary>
    /// Routes the incoming slash command to the matching handler.
    /// </summary>
    /// <param name="command">The incoming slash command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            Console.WriteLine($"Executing /{command.CommandName} with parameters: {FormatCommandOptions(command)}");

            if (!this.commands.TryGetValue(command.CommandName, out var slashCommand))
            {
                await command.RespondAsync("Unknown command.", ephemeral: true);
                return;
            }

            await slashCommand.HandleAsync(command);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Command /{command.CommandName} failed with parameters: {FormatCommandOptions(command)}");
            Console.WriteLine(exception);

            var errorMessage = CreateUserFacingErrorMessage(command, exception);
            var errorEmbed = this.embedFactory.CreateErrorEmbed(errorMessage);

            if (command.HasResponded)
            {
                await command.FollowupAsync(embed: errorEmbed, ephemeral: true);
                return;
            }

            await command.RespondAsync(embed: errorEmbed, ephemeral: true);
        }
    }

    private static string FormatCommandOptions(SocketSlashCommand command)
    {
        if (command.Data.Options.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", command.Data.Options.Select(FormatCommandOption));
    }

    private static string FormatCommandOption(SocketSlashCommandDataOption option)
    {
        if (option.Options.Count > 0)
        {
            return $"{option.Name}=({string.Join(", ", option.Options.Select(FormatCommandOption))})";
        }

        return $"{option.Name}={FormatCommandOptionValue(option.Value)}";
    }

    private static string FormatCommandOptionValue(object? value)
    {
        return value switch
        {
            null => "null",
            IUser user => $"{user.Username} ({user.Id})",
            IRole role => $"{role.Name} ({role.Id})",
            IChannel channel => $"{channel.Name} ({channel.Id})",
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string CreateUserFacingErrorMessage(SocketSlashCommand command, Exception exception)
    {
        var detail = GetExceptionDetail(exception);
        var parameterText = FormatCommandOptions(command);

        return
            $"Fehler beim Ausführen von `/{command.CommandName}`.{Environment.NewLine}" +
            $"Parameter: `{parameterText}`{Environment.NewLine}" +
            $"Details: {detail}";
    }

    private static string GetExceptionDetail(Exception exception)
    {
        return exception switch
        {
            InvalidOperationException invalidOperationException => GetMessageOrFallback(
                invalidOperationException,
                "Die Aktion konnte im aktuellen Zustand nicht ausgeführt werden."),

            ArgumentException argumentException => GetMessageOrFallback(
                argumentException,
                "Ein Command-Parameter ist ungültig oder fehlt."),

            FileNotFoundException fileNotFoundException => GetMessageOrFallback(
                fileNotFoundException,
                "Eine benötigte Datei wurde nicht gefunden."),

            HttpRequestException httpRequestException => GetMessageOrFallback(
                httpRequestException,
                "Die Verbindung zu einem externen Dienst ist fehlgeschlagen."),

            TaskCanceledException taskCanceledException => GetMessageOrFallback(
                taskCanceledException,
                "Die Anfrage hat zu lange gedauert und wurde abgebrochen."),

            JsonException jsonException => GetMessageOrFallback(
                jsonException,
                "Eine Antwort oder Datei konnte nicht gelesen werden."),

            _ => GetUnexpectedExceptionMessage(exception),
        };
    }

    private static string GetUnexpectedExceptionMessage(Exception exception)
    {
        var message = GetMessageOrFallback(
            exception,
            "Es ist ein unerwarteter Fehler aufgetreten. Details stehen im Bot-Log.");

        if (message == "Es ist ein unerwarteter Fehler aufgetreten. Details stehen im Bot-Log.")
        {
            return $"{message} ({exception.GetType().Name})";
        }

        return message;
    }

    private static string GetMessageOrFallback(Exception exception, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            return exception.Message;
        }

        if (!string.IsNullOrWhiteSpace(exception.InnerException?.Message))
        {
            return exception.InnerException.Message;
        }

        return fallbackMessage;
    }
}
