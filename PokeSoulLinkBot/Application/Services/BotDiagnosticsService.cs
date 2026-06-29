using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Keeps a bounded in-memory list of recent diagnostic events.
/// </summary>
public sealed class BotDiagnosticsService : IBotDiagnosticsService
{
    private const int DefaultCapacity = 50;

    private readonly object syncRoot = new object();
    private readonly int capacity;
    private readonly Queue<DiagnosticEvent> events = new Queue<DiagnosticEvent>();

    /// <summary>
    /// Initializes a new instance of the <see cref="BotDiagnosticsService"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of events retained in memory.</param>
    public BotDiagnosticsService(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        this.capacity = capacity;
    }

    /// <inheritdoc />
    public void Record(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (this.syncRoot)
        {
            this.events.Enqueue(diagnosticEvent);
            while (this.events.Count > this.capacity)
            {
                this.events.Dequeue();
            }
        }
    }

    /// <inheritdoc />
    public void RecordException(
        string severity,
        string source,
        string message,
        Exception exception,
        string? commandName = null,
        string? parameters = null,
        long? elapsedMilliseconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(exception);

        this.Record(new DiagnosticEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Severity = severity.Trim(),
            Source = source.Trim(),
            Message = message.Trim(),
            CommandName = string.IsNullOrWhiteSpace(commandName) ? null : commandName.Trim(),
            Parameters = string.IsNullOrWhiteSpace(parameters) ? null : parameters.Trim(),
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = GetSafeExceptionMessage(exception),
            ElapsedMilliseconds = elapsedMilliseconds,
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maxCount)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<DiagnosticEvent>();
        }

        lock (this.syncRoot)
        {
            return this.events
                .Reverse()
                .Take(maxCount)
                .Select(CloneEvent)
                .ToList();
        }
    }

    private static DiagnosticEvent CloneEvent(DiagnosticEvent diagnosticEvent)
    {
        return new DiagnosticEvent
        {
            OccurredAtUtc = diagnosticEvent.OccurredAtUtc,
            Severity = diagnosticEvent.Severity,
            Source = diagnosticEvent.Source,
            Message = diagnosticEvent.Message,
            CommandName = diagnosticEvent.CommandName,
            Parameters = diagnosticEvent.Parameters,
            ExceptionType = diagnosticEvent.ExceptionType,
            ExceptionMessage = diagnosticEvent.ExceptionMessage,
            ElapsedMilliseconds = diagnosticEvent.ElapsedMilliseconds,
        };
    }

    private static string? GetSafeExceptionMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.InnerException?.Message
            : exception.Message;

        return string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();
    }
}
