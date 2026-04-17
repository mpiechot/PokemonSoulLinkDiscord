using PokeSoulLinkBot.Bot.Assets;

namespace PokeSoulLinkBot.Bot.Factories;

/// <summary>
/// Creates image attachments for Discord embeds from local resource files.
/// </summary>
public sealed class EmbedImageFactory
{
    private readonly string resourcesDirectoryPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbedImageFactory"/> class.
    /// </summary>
    /// <param name="resourcesDirectoryPath">The absolute path to the resources directory.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="resourcesDirectoryPath"/> is null, empty, or whitespace.
    /// </exception>
    public EmbedImageFactory(string resourcesDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesDirectoryPath);
        this.resourcesDirectoryPath = resourcesDirectoryPath;
    }

    /// <summary>
    /// Creates an embed image for the run start command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateRunStartImage()
    {
        return this.CreateImage("run-start.png");
    }

    /// <summary>
    /// Creates an embed image for the run end command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateRunEndImage()
    {
        return this.CreateImage("run-end.png");
    }

    /// <summary>
    /// Creates an embed image for the catch command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateCatchImage()
    {
        return this.CreateImage("catch.png");
    }

    /// <summary>
    /// Creates an embed image for the death command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateDeathImage()
    {
        return this.CreateImage("death.png");
    }

    /// <summary>
    /// Creates an embed image for the status command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateStatusImage()
    {
        return this.CreateImage("status.png");
    }

    /// <summary>
    /// Creates an embed image for the stats command.
    /// </summary>
    /// <returns>The created embed image.</returns>
    public EmbedImage CreateStatsImage()
    {
        return this.CreateImage("stats.png");
    }

    private EmbedImage CreateImage(string fileName)
    {
        var filePath = Path.Combine(this.resourcesDirectoryPath, fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The resource image '{fileName}' was not found.", filePath);
        }

        var stream = File.OpenRead(filePath);
        var attachment = new Discord.FileAttachment(stream, fileName);
        var attachmentUrl = $"attachment://{fileName}";

        return new EmbedImage(attachment, attachmentUrl);
    }
}
