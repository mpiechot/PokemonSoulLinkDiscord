using System.Text.Json;
using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Bot.Handlers;

/// <summary>
/// Routes slash commands to their dedicated command implementations.
/// </summary>
public sealed class SlashCommandRouter
{
    private const long SlowCommandThresholdMilliseconds = 1500;
    private const long SlowAutocompleteThresholdMilliseconds = 500;

    private readonly IReadOnlyDictionary<string, ISlashCommand> commands;
    private readonly IBotDiagnosticsService diagnosticsService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlashCommandRouter"/> class.
    /// </summary>
    /// <param name="commands">The available slash commands.</param>
    /// <param name="embedFactory">The embed factory used for error messages.</param>
    /// <param name="diagnosticsService">The diagnostics service.</param>
    public SlashCommandRouter(
        IReadOnlyCollection<ISlashCommand> commands,
        EmbedFactory embedFactory,
        IBotDiagnosticsService diagnosticsService)
    {
        ArgumentNullException.ThrowIfNull(commands);

        this.commands = commands.ToDictionary(command => command.CommandName, StringComparer.OrdinalIgnoreCase);
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
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
        ISlashCommandResponse? response = null;

        try
        {
            Log.ForContext("EventName", "SlashCommandStarted").Information(
                "Executing slash command /{CommandName} with parameters: {Parameters}.",
                command.CommandName,
                parameterText);

            response = new DiscordSlashCommandResponse(command);
            if (!this.commands.TryGetValue(command.CommandName, out var slashCommand))
            {
                Log.ForContext("EventName", "SlashCommandUnknown").Warning(
                    "Unknown slash command /{CommandName}.",
                    command.CommandName);
                var errorEmbed = this.embedFactory.CreateErrorEmbed("Unknown command.");
                await response.SendAsync(embed: errorEmbed, ephemeral: true);
                return;
            }

            await response.DeferAsync();
            await slashCommand.HandleAsync(command, response);

            var elapsedMilliseconds = GetElapsedMilliseconds(startedAt);
            var completionLogger = Log.ForContext("EventName", "SlashCommandCompleted");
            if (elapsedMilliseconds >= SlowCommandThresholdMilliseconds)
            {
                completionLogger.Warning(
                    "Slash command /{CommandName} completed slowly in {ElapsedMilliseconds} ms.",
                    command.CommandName,
                    elapsedMilliseconds);
                this.diagnosticsService.Record(new DiagnosticEvent
                {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Severity = "Warning",
                    Source = "SlashCommandRouter",
                    Message = "Slash command completed slowly.",
                    CommandName = command.CommandName,
                    Parameters = parameterText,
                    ElapsedMilliseconds = elapsedMilliseconds,
                });
            }
            else
            {
                completionLogger.Information(
                    "Slash command /{CommandName} completed in {ElapsedMilliseconds} ms.",
                    command.CommandName,
                    elapsedMilliseconds);
            }
        }
        catch (Exception exception)
        {
            var elapsedMilliseconds = GetElapsedMilliseconds(startedAt);
            Log.ForContext("EventName", "SlashCommandFailed").Error(
                exception,
                "Slash command /{CommandName} failed after {ElapsedMilliseconds} ms with parameters: {Parameters}.",
                command.CommandName,
                elapsedMilliseconds,
                parameterText);
            this.diagnosticsService.RecordException(
                "Error",
                "SlashCommandRouter",
                "Slash command failed.",
                exception,
                command.CommandName,
                parameterText,
                elapsedMilliseconds);

            var errorMessage = CreateUserFacingErrorMessage(command, exception);
            var errorEmbed = this.embedFactory.CreateErrorEmbed(errorMessage);

            response ??= new DiscordSlashCommandResponse(command);
            await this.TrySendErrorResponseAsync(response, errorEmbed, exception);
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
            Log.ForContext("EventName", "AutocompleteStarted").Debug(
                "Handling autocomplete for /{CommandName}, option {OptionName}, value '{CurrentValue}'.",
                interaction.Data.CommandName,
                currentOptionName,
                currentValue);

            if (!this.commands.TryGetValue(interaction.Data.CommandName, out var slashCommand))
            {
                Log.ForContext("EventName", "AutocompleteUnknown").Warning(
                    "Unknown autocomplete command /{CommandName}.",
                    interaction.Data.CommandName);
                await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
                return;
            }

            await slashCommand.HandleAutocompleteAsync(interaction);

            var elapsedMilliseconds = GetElapsedMilliseconds(startedAt);
            var completionLogger = Log.ForContext("EventName", "AutocompleteCompleted");
            if (elapsedMilliseconds >= SlowAutocompleteThresholdMilliseconds)
            {
                completionLogger.Warning(
                    "Autocomplete for /{CommandName}, option {OptionName} completed slowly in {ElapsedMilliseconds} ms.",
                    interaction.Data.CommandName,
                    currentOptionName,
                    elapsedMilliseconds);
            }
            else
            {
                completionLogger.Debug(
                    "Autocomplete for /{CommandName}, option {OptionName} completed in {ElapsedMilliseconds} ms.",
                    interaction.Data.CommandName,
                    currentOptionName,
                    elapsedMilliseconds);
            }
        }
        catch (Exception exception)
        {
            Log.ForContext("EventName", "AutocompleteFailed").Error(
                exception,
                "Autocomplete for /{CommandName}, option {OptionName}, value '{CurrentValue}' failed after {ElapsedMilliseconds} ms.",
                interaction.Data.CommandName,
                currentOptionName,
                currentValue,
                GetElapsedMilliseconds(startedAt));
            this.diagnosticsService.RecordException(
                "Error",
                "SlashCommandRouter",
                "Autocomplete failed.",
                exception,
                interaction.Data.CommandName,
                $"{currentOptionName}={currentValue}",
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

    private async Task TrySendErrorResponseAsync(
        ISlashCommandResponse response,
        Embed errorEmbed,
        Exception originalException)
    {
        try
        {
            await response.SendAsync(embed: errorEmbed, ephemeral: true);
        }
        catch (Exception responseException)
        {
            Log.ForContext("EventName", "SlashCommandErrorResponseFailed").Warning(
                responseException,
                "Could not send error response for slash command /{CommandName}. OriginalException={OriginalExceptionType}.",
                response.CommandName,
                originalException.GetType().Name);
            this.diagnosticsService.RecordException(
                "Warning",
                "SlashCommandRouter",
                "Could not send slash-command error response.",
                responseException,
                response.CommandName);
        }
    }
}
