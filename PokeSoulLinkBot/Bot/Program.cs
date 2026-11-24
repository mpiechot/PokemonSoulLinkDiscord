using PokeSoulLinkBot.Bot.Hosting;
using Serilog;
using SoulLink;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--backup")
        {
            AutomationStore.Backup(args[1], args[2]);
            return;
        }

        if (args.Length == 2 && args[0] == "--migrate")
        {
            _ = new AutomationStore(args[1]);
            return;
        }

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
