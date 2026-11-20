using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SoulLink;

namespace SoulLink.Client;

public sealed class HostAdminClient : IDisposable
{
    private readonly HttpClient http;

    public HostAdminClient(string server, string key)
    {
        this.Server = server.Trim().TrimEnd('/');
        PlayerBundle.Validate(new PlayerProfile(Guid.NewGuid(), this.Server, "host"));
        key = key.Trim();
        if (key.Length < 32 || key.Any(char.IsControl)) throw new InvalidDataException("Gib den Admin-Schlüssel des Servers ein.");
        this.http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(this.Server + "/api/soullink/admin/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        this.http.DefaultRequestHeaders.Add("X-SoulLink-Admin", key);
    }

    public string Server { get; }

    public Task<RunGroup[]> GroupsAsync() => this.SendAsync<RunGroup[]>(HttpMethod.Get, "groups");

    public Task<RunGroup> CreateGroupAsync(string name) => this.SendAsync<RunGroup>(HttpMethod.Post, "groups", new CreateGroupRequest(name));

    public Task<HostRun[]> RunsAsync(Guid group) => this.SendAsync<HostRun[]>(HttpMethod.Get, $"groups/{group:D}/runs");

    public Task<HostRun> CreateRunAsync(Guid group, string name) =>
        this.SendAsync<HostRun>(HttpMethod.Post, $"groups/{group:D}/runs", new GroupRunRequest(name));

    public Task<HostPlayer[]> PlayersAsync(Guid group, Guid run) =>
        this.SendAsync<HostPlayer[]>(HttpMethod.Get, $"groups/{group:D}/runs/{run:D}/players");

    public Task<HostLinkEditor> LinkEditorAsync(Guid group, Guid run) =>
        this.SendAsync<HostLinkEditor>(HttpMethod.Get, $"groups/{group:D}/runs/{run:D}/links");

    public async Task SaveLinksAsync(Guid group, Guid run, EditLinksRequest changes)
    {
        using var result = await this.SendAsync<JsonDocument>(HttpMethod.Put, $"groups/{group:D}/runs/{run:D}/links", changes);
    }

    public async Task RebaselineAsync(Guid group, Guid run, Guid player)
    {
        using var result = await this.SendAsync<JsonDocument>(HttpMethod.Post, $"groups/{group:D}/runs/{run:D}/players/{player:D}/rebaseline");
    }

    public Task<HostRun> ChangeRunAsync(Guid group, Guid run, string action)
    {
        if (action is not ("start" or "pause" or "load" or "end")) throw new ArgumentOutOfRangeException(nameof(action));
        return this.SendAsync<HostRun>(HttpMethod.Post, $"groups/{group:D}/runs/{run:D}/{action}");
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? data = null) where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (data is not null) request.Content = JsonContent.Create(data, options: Protocol.Json);
        using var response = await this.http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("Der Server hat den Admin-Schlüssel nicht akzeptiert.");
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try { message = (await response.Content.ReadFromJsonAsync<HostError>(Protocol.Json))?.Error; }
            catch (Exception error) when (error is JsonException or NotSupportedException) { }
            throw new InvalidOperationException(message ?? (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound
                ? "Diese Funktion fehlt auf dem Run-Server. Bitte den Server aktualisieren."
                : $"Der Server antwortet mit HTTP {(int)response.StatusCode}."));
        }
        return await response.Content.ReadFromJsonAsync<T>(Protocol.Json) ?? throw new InvalidDataException("Die Serverantwort ist leer.");
    }

    public void Dispose() => this.http.Dispose();

    private sealed record HostError(string Error);
}
