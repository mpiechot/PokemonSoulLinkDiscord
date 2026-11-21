using System.Security.Cryptography;
using System.Text;

namespace SoulLink;

public sealed record PlayerUpdateManifest(int Format, string Version, long Size, string Sha256, string Signature)
{
    public const int MaximumManifestBytes = 8192;
    public const long MaximumPackageBytes = 512 * 1024 * 1024;

    public static bool TryVersion(string? value, out Version version)
    {
        if (value is { Length: <= 32 } && System.Version.TryParse(value, out var parsed) && parsed.Build >= 0 &&
            parsed.Revision < 0 && parsed.ToString(3) == value)
        {
            version = parsed;
            return true;
        }
        version = new Version();
        return false;
    }

    public byte[] SigningBytes() => Encoding.UTF8.GetBytes(FormattableString.Invariant(
        $"SoulLinkPlayerUpdate/v1\n{this.Version}\n{this.Size}\n{this.Sha256}\n"));

    public Version Verify(string trustedPublicKey)
    {
        if (this.Format != 1 || !TryVersion(this.Version, out var version) || this.Size is < 1 or > MaximumPackageBytes ||
            this.Sha256 is not { Length: 64 } || this.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')) ||
            string.IsNullOrWhiteSpace(this.Signature))
            throw new InvalidDataException("Die Update-Metadaten sind ungültig.");
        try
        {
            using var key = RSA.Create();
            byte[] publicKey = Convert.FromBase64String(trustedPublicKey);
            key.ImportSubjectPublicKeyInfo(publicKey, out int read);
            if (read != publicKey.Length || key.KeySize < 2048 || !key.VerifyData(this.SigningBytes(),
                Convert.FromBase64String(this.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new InvalidDataException("Die Update-Signatur ist ungültig.");
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            throw new InvalidDataException("Die Update-Signatur ist ungültig.", error);
        }
        return version;
    }

    public async Task VerifyFileAsync(Stream package, CancellationToken cancellationToken = default)
    {
        if (package.Length != this.Size) throw new InvalidDataException("Die Größe des Updates stimmt nicht.");
        package.Position = 0;
        byte[] actual = await SHA256.HashDataAsync(package, cancellationToken);
        if (!string.Equals(Convert.ToHexString(actual), this.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Die Prüfsumme des Updates stimmt nicht.");
        package.Position = 0;
    }

    public static string FileName(string version) =>
        TryVersion(version, out _) ? $"SoulLink-{version}.exe" : throw new InvalidDataException("Invalid player release version.");
}
