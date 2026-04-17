using Discord;

namespace PokeSoulLinkBot.Bot.Assets;

/// <summary>
/// Represents an embed image attachment that can be sent with a Discord message.
/// </summary>
public sealed class EmbedImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmbedImage"/> class.
    /// </summary>
    /// <param name="fileAttachment">The Discord file attachment.</param>
    /// <param name="attachmentUrl">The attachment URL used inside the embed.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fileAttachment"/> or <paramref name="attachmentUrl"/> is <see langword="null"/>.
    /// </exception>
    public EmbedImage(FileAttachment? fileAttachment, string attachmentUrl)
    {
        this.FileAttachment = fileAttachment ?? throw new ArgumentNullException(nameof(fileAttachment));
        this.AttachmentUrl = attachmentUrl ?? throw new ArgumentNullException(nameof(attachmentUrl));
    }

    /// <summary>
    /// Gets the Discord file attachment.
    /// </summary>
    public FileAttachment FileAttachment { get; }

    /// <summary>
    /// Gets the attachment URL used by the embed.
    /// </summary>
    public string AttachmentUrl { get; }
}
