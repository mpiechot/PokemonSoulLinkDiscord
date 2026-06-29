using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Stores recent diagnostic events that can be reported after an error occurred.
/// </summary>
public interface IBotDiagnosticsService
{
    /// <summary>
    /// Records a diagnostic event.
    /// </summary>
    /// <param name="diagnosticEvent">The event to record.</param>
    void Record(DiagnosticEvent diagnosticEvent);

    /// <summary>
    /// Records an exception as a diagnostic event.
    /// </summary>
    /// <param name="severity">The event severity.</param>
    /// <param name="source">The component that observed the event.</param>
    /// <param name="message">The short event message.</param>
    /// <param name="exception">The observed exception.</param>
    /// <param name="commandName">The related command name, if any.</param>
    /// <param name="parameters">The related command parameters, if any.</param>
    /// <param name="elapsedMilliseconds">The elapsed duration in milliseconds, if any.</param>
    void RecordException(
        string severity,
        string source,
        string message,
        Exception exception,
        string? commandName = null,
        string? parameters = null,
        long? elapsedMilliseconds = null);

    /// <summary>
    /// Gets the most recent diagnostic events, newest first.
    /// </summary>
    /// <param name="maxCount">The maximum number of events to return.</param>
    /// <returns>The recent diagnostic events.</returns>
    IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maxCount);
}
