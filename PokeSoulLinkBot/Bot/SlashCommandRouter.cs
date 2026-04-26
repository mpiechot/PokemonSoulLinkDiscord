using System.Text.Json;
using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using Serilog;

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

        var startedAt = DateTimeOffset.UtcNow;
        var parameterText = FormatCommandOptions(command);

        try
        {
            Log.Information(
                "Executing slash command /{CommandName} with parameters: {Parameters}.",
                command.CommandName,
                parameterText);

            if (!this.commands.TryGetValue(command.CommandName, out var slashCommand))
            {
                Log.Warning("Unknown slash command /{CommandName}.", command.CommandName);
                var errorEmbed = this.embedFactory.CreateErrorEmbed("Unknown command.");
                await SlashCommandResponse.SendAsync(command, embed: errorEmbed, ephemeral: true);
                return;
            }

            await command.DeferAsync();
            await slashCommand.HandleAsync(command);

            Log.Information(
                "Slash command /{CommandName} completed in {ElapsedMilliseconds} ms.",
                command.CommandName,
                GetElapsedMilliseconds(startedAt));
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Slash command /{CommandName} failed after {ElapsedMilliseconds} ms with parameters: {Parameters}.",
                command.CommandName,
                GetElapsedMilliseconds(startedAt),
                parameterText);

            var errorMessage = CreateUserFacingErrorMessage(command, exception);
            var errorEmbed = this.embedFactory.CreateErrorEmbed(errorMessage);

            await SlashCommandResponse.SendAsync(command, embed: errorEmbed, ephemeral: true);
        }
    }

    /// <summary>
    /// Routes the incoming autocomplete interaction to the matching command handler.
    /// </summary>
    /// <param name="interaction">The incoming autocomplete interaction.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var startedAt = DateTimeOffset.UtcNow;
        var currentOptionName = interaction.Data.Current.Name;
        var currentValue = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        try
        {
            Log.Debug(
                "Handling autocomplete for /{CommandName}, option {OptionName}, value '{CurrentValue}'.",
                interaction.Data.CommandName,
                currentOptionName,
                currentValue);

            if (!this.commands.TryGetValue(interaction.Data.CommandName, out var slashCommand))
            {
                Log.Warning("Unknown autocomplete command /{CommandName}.", interaction.Data.CommandName);
                await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
                return;
            }

            await slashCommand.HandleAutocompleteAsync(interaction);

            Log.Debug(
                "Autocomplete for /{CommandName}, option {OptionName} completed in {ElapsedMilliseconds} ms.",
                interaction.Data.CommandName,
                currentOptionName,
                GetElapsedMilliseconds(startedAt));
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Autocomplete for /{CommandName}, option {OptionName}, value '{CurrentValue}' failed after {ElapsedMilliseconds} ms.",
                interaction.Data.CommandName,
                currentOptionName,
                currentValue,
                GetElapsedMilliseconds(startedAt));

            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            }
        }
    }

    private static long GetElapsedMilliseconds(DateTimeOffset startedAt)
    {
        return (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
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
