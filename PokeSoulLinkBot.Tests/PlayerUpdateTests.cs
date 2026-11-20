using System.Net;
using System.Security.Cryptography;
using System.Text;
using SoulLink;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PlayerUpdateTests
{
    [Fact]
    public async Task DownloadsOnlyANewerSignedPackageAndVerifiesItsBytes()
    {
        using var key = RSA.Create(2048);
        byte[] package = Encoding.UTF8.GetBytes("test package");
        var signed = Sign(key, "1.1.0", package);
        using var http = MockServer(signed, package);
        var client = Client(http, key, new Version(1, 0, 0));
        Assert.Equal(signed, await client.CheckAsync(CancellationToken.None));
        using var output = new MemoryStream();
        await client.DownloadAsync(signed, output, CancellationToken.None);
        Assert.Equal(package, output.ToArray());
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("2.0.0")]
    public async Task CurrentAndNewerInstallationsDoNotOfferTheRelease(string installed)
    {
        using var key = RSA.Create(2048);
        byte[] package = [1, 2, 3];
        using var http = MockServer(Sign(key, "1.1.0", package), package);
        Assert.Null(await Client(http, key, Version.Parse(installed)).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTamperedMetadataAndCorruptDownloads()
    {
        using var key = RSA.Create(2048);
        byte[] package = [1, 2, 3];
        var signed = Sign(key, "1.1.0", package);
        using var tamper = MockServer(signed with { Version = "2.0.0" }, package);
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(tamper, key, new Version(1, 0, 0)).CheckAsync(CancellationToken.None));

        using var corrupt = MockServer(signed, [1, 2, 4]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client(corrupt, key, new Version(1, 0, 0)).DownloadAsync(signed, output, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsSignaturesFromAnotherKeyAndOversizedMetadata()
    {
        using var key = RSA.Create(2048);
        using var other = RSA.Create(2048);
        byte[] package = [1, 2, 3];
        using var http = MockServer(Sign(other, "1.1.0", package), package);
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(http, key, new Version(1, 0, 0)).CheckAsync(CancellationToken.None));
        using var oversized = new HttpClient(new Response(_ => new ByteArrayContent(new byte[PlayerUpdateManifest.MaximumManifestBytes + 1])));
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(oversized, key, new Version(1, 0, 0)).CheckAsync(CancellationToken.None));
    }

    private static PlayerReleaseClient Client(HttpClient http, RSA key, Version installed) =>
        new(http, new Uri("https://example.test"), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), installed);

    private static PlayerUpdateManifest Sign(RSA key, string version, byte[] package)
    {
        var manifest = new PlayerUpdateManifest(1, version, package.Length, Convert.ToHexString(SHA256.HashData(package)), "");
        return manifest with
        {
            Signature = Convert.ToBase64String(key.SignData(manifest.SigningBytes(), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)),
        };
    }

    private static HttpClient MockServer(PlayerUpdateManifest manifest, byte[] package)
    {
        return new HttpClient(new Response(path => path switch
        {
            "/api/soullink/player-update/manifest.json" => new StringContent(Protocol.Serialize(manifest)),
            "/api/soullink/player-update/SoulLink-1.1.0.exe" => new ByteArrayContent(package),
            _ => throw new InvalidDataException("Unexpected download URL."),
        }));
    }

    private sealed class Response(Func<string, HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request.RequestUri!.AbsolutePath) });
    }
}
