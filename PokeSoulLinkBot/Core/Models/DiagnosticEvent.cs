namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a recent diagnostic event that can help troubleshoot bot problems after they occurred.
/// </summary>
public sealed class DiagnosticEvent
{
    /// <summary>
    /// Gets or sets the UTC date and time when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the event severity.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the component that recorded the event.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the short event message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the slash command name related to the event, if any.
    /// </summary>
    public string? CommandName { get; set; }

    /// <summary>
    /// Gets or sets the formatted command parameters related to the event, if any.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the exception type related to the event, if any.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets a safe exception message related to the event, if any.
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Gets or sets the elapsed command duration in milliseconds, if available.
    /// </summary>
    public long? ElapsedMilliseconds { get; set; }
}
