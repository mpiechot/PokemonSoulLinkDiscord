using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class BotDiagnosticsServiceTests
{
    [Fact]
    public void GetRecentEvents_ShouldReturnNewestEventsWithinCapacity()
    {
        var service = new BotDiagnosticsService(capacity: 2);

        service.Record(new DiagnosticEvent { Severity = "Warning", Source = "test", Message = "first" });
        service.Record(new DiagnosticEvent { Severity = "Error", Source = "test", Message = "second" });
        service.Record(new DiagnosticEvent { Severity = "Warning", Source = "test", Message = "third" });

        var events = service.GetRecentEvents(10);

        Assert.Equal(2, events.Count);
        Assert.Equal("third", events[0].Message);
        Assert.Equal("second", events[1].Message);
    }

    [Fact]
    public void RecordException_ShouldStoreCommandContextAndExceptionType()
    {
        var service = new BotDiagnosticsService();

        service.RecordException(
            "Error",
            "SlashCommandRouter",
            "Slash command failed.",
            new InvalidOperationException("No active run."),
            "status",
            "none",
            42);

        var diagnosticEvent = Assert.Single(service.GetRecentEvents(5));

        Assert.Equal("Error", diagnosticEvent.Severity);
        Assert.Equal("SlashCommandRouter", diagnosticEvent.Source);
        Assert.Equal("status", diagnosticEvent.CommandName);
        Assert.Equal("none", diagnosticEvent.Parameters);
        Assert.Equal("InvalidOperationException", diagnosticEvent.ExceptionType);
        Assert.Equal("No active run.", diagnosticEvent.ExceptionMessage);
        Assert.Equal(42, diagnosticEvent.ElapsedMilliseconds);
    }
}
