using System.Text.Json;

namespace SoulLink;

public sealed class PlayerReleaseClient(HttpClient http, Uri origin, string trustedPublicKey, Version installed)
{
    public async Task<PlayerUpdateManifest?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(this.Url("manifest.json"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] content = new byte[PlayerUpdateManifest.MaximumManifestBytes + 1];
        int read = await stream.ReadAtLeastAsync(content, content.Length, false, cancellationToken);
        if (read == content.Length) throw new InvalidDataException("Die Update-Metadaten sind zu groß.");
        var manifest = JsonSerializer.Deserialize<PlayerUpdateManifest>(content.AsSpan(0, read), Protocol.Json)
            ?? throw new InvalidDataException("Die Update-Metadaten fehlen.");
        return manifest.Verify(trustedPublicKey) > installed ? manifest : null;
    }

    public async Task DownloadAsync(PlayerUpdateManifest manifest, Stream destination, CancellationToken cancellationToken)
    {
        if (manifest.Verify(trustedPublicKey) <= installed) throw new InvalidDataException("Die Update-Version ist nicht neuer.");
        using var response = await http.GetAsync(this.Url(PlayerUpdateManifest.FileName(manifest.Version)),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != manifest.Size)
            throw new InvalidDataException("Die Größe des Updates stimmt nicht.");
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > manifest.Size) throw new InvalidDataException("Die Größe des Updates stimmt nicht.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        await manifest.VerifyFileAsync(destination, cancellationToken);
    }

    private Uri Url(string file)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/" ||
            origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidDataException("Die Update-Adresse muss eine HTTPS-Serveradresse sein.");
        return new Uri(origin, $"/api/soullink/player-update/{file}");
    }
}
