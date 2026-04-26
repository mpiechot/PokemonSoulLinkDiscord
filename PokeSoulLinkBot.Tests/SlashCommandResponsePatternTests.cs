using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class SlashCommandResponsePatternTests
{
    [Fact]
    public void Router_ShouldDeferKnownSlashCommandsBeforeExecutingHandler()
    {
        var routerSource = ReadSourceFile("PokeSoulLinkBot", "Bot", "SlashCommandRouter.cs");

        var deferIndex = routerSource.IndexOf("await command.DeferAsync();", StringComparison.Ordinal);
        var handleIndex = routerSource.IndexOf("await slashCommand.HandleAsync(command);", StringComparison.Ordinal);

        Assert.True(deferIndex >= 0, "SlashCommandRouter must acknowledge known slash commands with DeferAsync.");
        Assert.True(handleIndex >= 0, "SlashCommandRouter must execute the selected slash command handler.");
        Assert.True(deferIndex < handleIndex, "SlashCommandRouter must defer before command handlers can call slow services.");
    }

    [Fact]
    public void SlashCommandHandlers_ShouldUseCentralResponseHelperForInitialResponses()
    {
        var commandsDirectory = Path.Combine(GetRepositoryRoot(), "PokeSoulLinkBot", "Bot", "Commands");
        var commandFiles = Directory.GetFiles(commandsDirectory, "*Command.cs")
            .Where(file => Path.GetFileName(file) != "ISlashCommand.cs");
        var violations = new List<string>();

        foreach (var commandFile in commandFiles)
        {
            var source = File.ReadAllText(commandFile);
            var handleAsyncBody = ExtractMethodBody(source, "public async Task HandleAsync(SocketSlashCommand command)");

            if (handleAsyncBody.Contains(".RespondAsync(", StringComparison.Ordinal)
                || handleAsyncBody.Contains(".RespondWithFileAsync(", StringComparison.Ordinal))
            {
                violations.Add(Path.GetFileName(commandFile));
            }
        }

        Assert.Empty(violations);
    }

    private static string ReadSourceFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(new[] { GetRepositoryRoot() }.Concat(pathParts).ToArray()));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "PokeSoulLinkBot"))
                && Directory.Exists(Path.Combine(directory.FullName, "PokeSoulLinkBot.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find method signature '{signature}'.");

        var openingBraceIndex = source.IndexOf('{', signatureIndex);
        Assert.True(openingBraceIndex >= 0, $"Could not find method body for '{signature}'.");

        var braceDepth = 0;
        for (var index = openingBraceIndex; index < source.Length; index++)
        {
            braceDepth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (braceDepth == 0)
            {
                return source.Substring(openingBraceIndex, index - openingBraceIndex + 1);
            }
        }

        throw new InvalidOperationException($"Could not parse method body for '{signature}'.");
    }
}
