using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SoulLink;
using SoulLink.Demo;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string data = Path.Combine(root, "artifacts", "demo-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(data);
int serverPort = FreePort(), uiPort = FreePort(), bridgePort = FreePort();
string server = $"http://127.0.0.1:{serverPort}", ui = $"http://127.0.0.1:{uiPort}";
string adminKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(args.Contains("--verify") ? 2 : 120));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
using var admin = new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(8) };
admin.DefaultRequestHeaders.Add("X-SoulLink-Admin", adminKey);
using var host = Start("PokeSoulLinkBot", "PokeSoulLinkBot.dll", "--SoulLink:EnableDiscordEvents=false",
    "--SoulLink:ListenUrl=" + server, "--SoulLink:DatabasePath=" + Path.Combine(data, "server.sqlite"),
    "--SoulLink:PersistencePath=" + Path.Combine(data, "legacy.json"), "--SoulLink:AdminKey=" + adminKey);
using var client = Start("SoulLink.Client", "SoulLink.Client.dll", "--UiPort=" + uiPort,
    "--BridgePort=" + bridgePort, "--SettingsPath=" + Path.Combine(data, "client.json"));
var hubs = new List<HubConnection>();
try
{
    await WaitFor(server + "/health/soullink");
    await WaitFor(ui + "/api/status");
    var run = await Post<AutomationRun>(admin, "/api/soullink/admin/runs", new CreateRun("demo", "Three-player Ruby demo", DeathRule: DeathRule.ZeroHpOutsideBattle));
    await Post<object>(http, ui + "/api/join", new { server, invitation = run.Invitation, name = "Marco (demo)" });
    var sessions = new List<SessionInfo>();
    foreach (string name in new[] { "Alex (demo)", "Sam (demo)" })
    {
        Guid id = Guid.NewGuid();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Post<Registration>(http, server + "/api/soullink/join", new JoinRequest(run.Invitation, name, id, token, "AXVD"));
        var hub = new HubConnectionBuilder().WithUrl(server + "/soullink", options =>
        {
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
        }).AddJsonProtocol(options => options.PayloadSerializerOptions = Protocol.Json).Build();
        hubs.Add(hub);
        await hub.StartAsync(lifetime.Token);
        var session = await hub.InvokeAsync<SessionInfo>("Hello", new Hello(id), lifetime.Token);
        sessions.Add(session);
        var baseline = SyntheticPokemon.Frame(1, SyntheticPokemon.Raw((uint)(100 + hubs.Count), 1)).Parse();
        await hub.InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), session.SessionId, 1, true, baseline), lifetime.Token);
    }
    using var bridge = new TcpClient();
    await bridge.ConnectAsync(IPAddress.Loopback, bridgePort, lifetime.Token);
    long frame = 1;
    var current = SyntheticPokemon.Frame(frame, SyntheticPokemon.Raw(100, 1));
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].Players.All(p => p.LastSnapshot is not null));
    await Post<AutomationRun>(admin, $"/api/soullink/admin/runs/{run.Id}/start", new RunAction("demo"));
    Console.WriteLine("Baseline accepted for all three players. Run started.");
    for (int i = 0; i < 2; i++)
    {
        var snapshot = SyntheticPokemon.Frame(2, SyntheticPokemon.Raw((uint)(101 + i), 1), SyntheticPokemon.Raw((uint)(201 + i), 280, 1, box: i == 1)).Parse();
        await hubs[i].InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), sessions[i].SessionId, 2, false, snapshot), lifetime.Token);
    }
    current = SyntheticPokemon.Frame(++frame, SyntheticPokemon.Raw(100, 1), SyntheticPokemon.Raw(200, 280, 1));
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].Encounters.Any(e => e.GroupId is not null));
    Console.WriteLine("Verified: catch → client → authenticated SignalR → SQLite → three-player link (including direct-to-box catch).");
    Console.WriteLine("Client UI: " + ui);
    Console.WriteLine("Isolated demo data: " + data);
    if (!args.Contains("--headless") && !args.Contains("--verify")) Process.Start(new ProcessStartInfo(ui) { UseShellExecute = true });
    current = SyntheticPokemon.Frame(++frame, SyntheticPokemon.Raw(100, 1), SyntheticPokemon.Raw(200, 281, 1, experience: 126));
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].History.Any(e => e.Kind == "evolved"));
    Console.WriteLine("Verified: evolution preserves identity and link membership.");
    var isolatedRun = await Post<AutomationRun>(admin, "/api/soullink/admin/runs", new CreateRun("isolated-guild", "Isolated run"));
    Guid isolatedClientId = Guid.NewGuid();
    string isolatedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await Post<Registration>(http, server + "/api/soullink/join", new JoinRequest(isolatedRun.Invitation, "Isolated player", isolatedClientId, isolatedToken, "AXVD"));
    await using (var isolatedHub = new HubConnectionBuilder().WithUrl(server + "/soullink", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(isolatedToken);
        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
        options.SkipNegotiation = true;
    }).AddJsonProtocol(options => options.PayloadSerializerOptions = Protocol.Json).Build())
    {
        await isolatedHub.StartAsync(lifetime.Token);
        var isolatedSession = await isolatedHub.InvokeAsync<SessionInfo>("Hello", new Hello(isolatedClientId), lifetime.Token);
        if ((await isolatedHub.InvokeAsync<AutomationRun>("Status", lifetime.Token)).Id != isolatedRun.Id ||
            (await hubs[0].InvokeAsync<AutomationRun>("Status", lifetime.Token)).Id != run.Id)
            throw new InvalidDataException("Run isolation failed.");
        bool rejected = false;
        var fullInventory = new List<RawPokemon>();
        for (int slot = 0; slot < 6; slot++) fullInventory.Add(SyntheticPokemon.Raw((uint)slot, 392, slot));
        for (int box = 0; box < 14; box++)
            for (int slot = 0; slot < 30; slot++)
                fullInventory.Add(SyntheticPokemon.Raw((uint)(100 + box * 30 + slot), 392, slot, box: true) with { Box = box });
        var fullResult = await isolatedHub.InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), isolatedSession.SessionId, 1, true,
            SyntheticPokemon.Frame(1, fullInventory.ToArray()).Parse()), lifetime.Token);
        if (!fullResult.Accepted || (await isolatedHub.InvokeAsync<AutomationRun>("Status", lifetime.Token)).Players.Single().Pokemon.Count != 426)
            throw new InvalidDataException("Full-inventory transport failed.");
        await Task.Delay(200, lifetime.Token);
        try
        {
            await isolatedHub.InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), sessions[0].SessionId, 1, true,
                SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(333, 1)).Parse()), lifetime.Token);
        }
        catch (Microsoft.AspNetCore.SignalR.HubException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("A foreign session was not rejected.");
    }
    Console.WriteLine("Verified: full 426-Pokémon inventories cross SignalR; connections remain isolated and reject foreign sessions.");
    using (var status = JsonDocument.Parse(await http.GetStringAsync(ui + "/api/status", lifetime.Token)))
    {
        var pokemon = status.RootElement.GetProperty("snapshot").GetProperty("pokemon")[0];
        if (pokemon.GetProperty("container").GetString() != "Party")
            throw new InvalidDataException("Local UI API must expose placement enums as strings.");
    }
    if (args.Contains("--verify")) return;
    Console.WriteLine("Press Ctrl+C to close only these demo processes. No Discord connection or real save is used.");
    while (!lifetime.IsCancellationRequested)
    {
        await Task.Delay(2000, lifetime.Token);
        current = current with { Frame = ++frame, PlaySeconds = frame };
        await Send(current);
        for (int i = 0; i < hubs.Count; i++) await hubs[i].InvokeAsync<string>("Heartbeat", sessions[i].SessionId, lifetime.Token);
    }

    async Task Send(BridgeFrame value) => await bridge.GetStream().WriteAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(value) + "\n"), lifetime.Token);
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
finally
{
    foreach (var hub in hubs) await hub.DisposeAsync();
    if (!client.HasExited) client.Kill(true);
    if (!host.HasExited) host.Kill(true);
    await client.WaitForExitAsync();
    await host.WaitForExitAsync();
}

Process Start(string folder, string dll, params string[] arguments)
{
    var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, folder), CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(Path.Combine(root, folder, "bin", "Debug", "net9.0", dll));
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start demo process.");
    process.OutputDataReceived += (_, e) => { if (e.Data?.Contains("fail:", StringComparison.OrdinalIgnoreCase) == true) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

async Task WaitFor(string endpoint) => await WaitUntil(async () => { try { return (await http.GetAsync(endpoint, lifetime.Token)).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } });

async Task WaitUntil(Func<Task<bool>> condition)
{
    for (int i = 0; i < 60; i++) { if (await condition()) return; await Task.Delay(500, lifetime.Token); }
    throw new InvalidOperationException("Timed out waiting for the expected demo state.");
}

static async Task<T> Post<T>(HttpClient client, string url, object body)
{
    using var result = await client.PostAsJsonAsync(url, body, Protocol.Json);
    if (!result.IsSuccessStatusCode) throw new InvalidOperationException("Demo API rejected request: " + await result.Content.ReadAsStringAsync());
    if (typeof(T) == typeof(object)) return default!;
    return await result.Content.ReadFromJsonAsync<T>(Protocol.Json) ?? throw new InvalidDataException("Empty demo response.");
}

static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
