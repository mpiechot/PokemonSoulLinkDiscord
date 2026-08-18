using PokeSoulLinkBot.Bot.Hosting;
using Serilog;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            await BotHost.RunAsync(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "PokeSoulLinkBot terminated unexpectedly.");
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
