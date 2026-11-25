using System.Buffers.Text;
using System.Text.Json;
using SoulLink;

namespace SoulLink.Client;

public static class PlayerInvitation
{
    private const string Prefix = "SL1-";
    private const int MaxCodeLength = 4096;

    public static bool ContainsServer(string value) => value.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encode(PlayerProfile profile)
    {
        PlayerBundle.Validate(profile);
        var invite = new Details(profile.Server, profile.Invitation, profile.Label);
        string code = Prefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(invite, Protocol.Json));
        if (code.Length > MaxCodeLength) throw new InvalidDataException("Die Einladung ist zu lang.");
        return code;
    }

    public static PlayerProfile Read(string code, string fallbackServer)
    {
        code = code.Trim();
        Details invitation;
        if (ContainsServer(code))
        {
            if (code.Length > MaxCodeLength) throw new InvalidDataException("Die Einladung ist zu lang.");
            try
            {
                invitation = JsonSerializer.Deserialize<Details>(Base64Url.DecodeFromChars(code.AsSpan(Prefix.Length)), Protocol.Json)
                    ?? throw new InvalidDataException("Die Einladung ist leer.");
            }
            catch (Exception error) when (error is FormatException or JsonException)
            {
                throw new InvalidDataException("Die Einladung ist unvollständig oder beschädigt. Kopiere sie erneut.", error);
            }
        }
        else
        {
            invitation = new Details(fallbackServer, code, null);
        }

        var profile = new PlayerProfile(Guid.NewGuid(), invitation.Server?.Trim().TrimEnd('/') ?? "", invitation.Invitation ?? "", Label: invitation.Label);
        PlayerBundle.Validate(profile);
        return profile;
    }

    private sealed record Details(string Server, string Invitation, string? Label);
}
