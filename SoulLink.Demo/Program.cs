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
using SoulLink.Client;
using SoulLink.Demo;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
bool verifyOnly = args.Contains("--verify");
string dataRoot = verifyOnly ? Path.GetTempPath() : Path.Combine(root, "artifacts");
string data = Directory.CreateDirectory(Path.Combine(dataRoot, "demo-" + Guid.NewGuid().ToString("N"))).FullName;
int serverPort = FreePort(), uiPort = FreePort(), bridgePort = FreePort();
string server = $"http://127.0.0.1:{serverPort}", ui = $"http://127.0.0.1:{uiPort}";
string adminKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(verifyOnly ? 2 : 120));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
using var admin = new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(8) };
admin.DefaultRequestHeaders.Add("X-SoulLink-Admin", adminKey);
using var host = Start("PokeSoulLinkBot", "PokeSoulLinkBot.dll", "--SoulLink:EnableDiscordEvents=false",
    "--SoulLink:ListenUrl=" + server, "--SoulLink:DatabasePath=" + Path.Combine(data, "server.sqlite"),
    "--SoulLink:PersistencePath=" + Path.Combine(data, "legacy.json"), "--SoulLink:AdminKey=" + adminKey);
using var client = Start("SoulLink.Client", "SoulLink.Client.dll", "--UiPort=" + uiPort,
    "--BridgePort=" + bridgePort, "--AllowSyntheticFrames=true", "--SettingsPath=" + Path.Combine(data, "client.json"),
    "--EnableLocalRun=" + verifyOnly);
var hubs = new List<HubConnection>();
try
{
    await WaitFor(server + "/health/soullink");
    await WaitFor(ui + "/api/status");
    if (verifyOnly)
    {
        using var hostTools = new HostAdminClient(server, adminKey);
        var monday = await hostTools.CreateGroupAsync("Monday group");
        var friday = await hostTools.CreateGroupAsync("Friday group");
        var mondayRun = await hostTools.CreateRunAsync(monday.Id, "Monday Ruby");
        var fridayRun = await hostTools.CreateRunAsync(friday.Id, "Friday Ruby");
        if ((await hostTools.GroupsAsync()).Length != 2 ||
            (await hostTools.RunsAsync(monday.Id)).Single() != mondayRun ||
            (await hostTools.RunsAsync(friday.Id)).Single() != fridayRun)
            throw new InvalidOperationException("Discord-free groups did not retain separate runs.");
        Console.WriteLine("Verified: host API creates and lists isolated groups with Discord disabled.");
    }
    var run = await Post<AutomationRun>(admin, "/api/soullink/admin/runs", new CreateRun("demo", "Three-player Ruby demo"));
    await Post<object>(http, ui + "/api/join", new { server, invitation = run.Invitation, name = "Marco (demo)", marker = "Star" });
    var sessions = new List<SessionInfo>();
    foreach (string name in new[] { "Alex (demo)", "Sam (demo)" })
    {
        Guid id = Guid.NewGuid();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Post<Registration>(http, server + "/api/soullink/join", new JoinRequest(run.Invitation, name, id, token, "AXVD",
            Marker: hubs.Count == 0 ? PlayerMarker.Moon : PlayerMarker.Diamond));
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
        var baseline = (SyntheticPokemon.Frame(1, SyntheticPokemon.Raw((uint)(100 + hubs.Count), 1)) with
        {
            MapNumber = 15 + hubs.Count,
        }).Parse();
        await hub.InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), session.SessionId, 1, true, baseline), lifetime.Token);
    }
    using var bridge = new TcpClient();
    await bridge.ConnectAsync(IPAddress.Loopback, bridgePort, lifetime.Token);
    long frame = 1;
    var current = SyntheticPokemon.Frame(frame, SyntheticPokemon.Raw(100, 1)) with { BadgeMask = 0 };
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].Players.All(p => p.LastSnapshot is not null));
    await Post<AutomationRun>(admin, $"/api/soullink/admin/runs/{run.Id}/start", new RunAction("demo"));
    Console.WriteLine("Baseline accepted for all three players. Run started.");
    for (int i = 0; i < 2; i++)
    {
        var snapshot = (SyntheticPokemon.Frame(2, SyntheticPokemon.Raw((uint)(101 + i), 1),
            SyntheticPokemon.Raw((uint)(201 + i), 280, 1, box: i == 1)) with
        { MapNumber = 16 + i }).Parse();
        await hubs[i].InvokeAsync<ObservationResult>("Observe", new Observation(Guid.NewGuid(), sessions[i].SessionId, 2, false, snapshot), lifetime.Token);
    }
    current = SyntheticPokemon.Frame(++frame, SyntheticPokemon.Raw(100, 1), SyntheticPokemon.Raw(200, 280, 1)) with { BadgeMask = 0 };
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].Encounters.Any(e => e.GroupId is not null));
    Console.WriteLine("Verified: catch → client → authenticated SignalR → SQLite → three-player link (including direct-to-box catch).");
    Console.WriteLine("Client UI: " + ui);
    Console.WriteLine("Isolated demo data: " + data);
    if (!args.Contains("--headless") && !verifyOnly) Process.Start(new ProcessStartInfo(ui) { UseShellExecute = true });
    current = SyntheticPokemon.Frame(++frame, SyntheticPokemon.Raw(100, 1), SyntheticPokemon.Raw(200, 281, 1, experience: 126)) with { BadgeMask = 0 };
    await Send(current);
    await WaitUntil(async () => (await admin.GetFromJsonAsync<List<AutomationRun>>("/api/soullink/admin/runs/demo", Protocol.Json))![0].History.Any(e => e.Kind == "evolved"));
    Console.WriteLine("Verified: evolution preserves identity and link membership.");
    current = current with { Frame = ++frame, PlaySeconds = frame, BadgeMask = 1 };
    await Send(current);
    await hubs[1].InvokeAsync<string>("HeartbeatWithBridge", sessions[1].SessionId, false, lifetime.Token);
    await WaitUntil(async () =>
    {
        using var dashboard = JsonDocument.Parse(await http.GetStringAsync(ui + "/api/status", lifetime.Token));
        if (!dashboard.RootElement.TryGetProperty("run", out var state) || state.ValueKind != JsonValueKind.Object) return false;
        var players = state.GetProperty("players").EnumerateArray().ToArray();
        return !dashboard.RootElement.GetProperty("isLocalRun").GetBoolean() && state.GetProperty("id").GetGuid() == run.Id
            && players.Any(p => p.GetProperty("marker").GetString() == "Star" && p.GetProperty("area").GetInt32() == 16)
            && players.Any(p => p.GetProperty("marker").GetString() == "Diamond" && p.GetProperty("area").GetInt32() == 17
                && p.GetProperty("connection").GetString() == "Stale")
            && state.GetProperty("links").GetArrayLength() == 1
            && state.GetProperty("history").EnumerateArray().Any(entry => entry.GetProperty("kind").GetString() == "badge");
    });
    Console.WriteLine("Verified: dashboard transmits player-selected markers, distinct map regions, stale positions, links and badge events.");
    if (verifyOnly)
    {
        const string rule = "Keine Heilitems im Arenakampf.";
        await hubs[0].InvokeAsync("AddRule", rule, lifetime.Token);
        var rulesForPartner = (await hubs[1].InvokeAsync<AutomationRun>("Status", lifetime.Token)).AdditionalRules;
        if (!rulesForPartner.SequenceEqual(new[] { rule }))
            throw new InvalidOperationException("The second player did not receive the shared rule.");
        var hub = hubs[0];
        var session = sessions[0];
        var eligibility = await hub.InvokeAsync<CatchCheck>("CheckCatch", "Combusken", lifetime.Token);
        if (eligibility.Pokemon != "Jungglut" || eligibility.PreviouslyCaught is null)
            throw new InvalidDataException("German/English catch family lookup did not cross SignalR.");
        await hub.InvokeAsync("ChangeArena", 1, true, lifetime.Token);
        if (!(await hub.InvokeAsync<AutomationRun>("Status", lifetime.Token)).History.Any(e => e.Kind == "arena-completed"))
            throw new InvalidDataException("Arena completion did not reach the shared feed.");
        await hub.InvokeAsync("ChangeArena", 1, false, lifetime.Token);
        var trace = new BattleTrace(1, 25, 33, "MAX");
        for (int sequence = 3; sequence <= 5; sequence++)
        {
            await Task.Delay(200, lifetime.Token);
            var snapshot = SyntheticPokemon.Frame(sequence, SyntheticPokemon.Raw(101, 1), SyntheticPokemon.Raw(201, 280, 1, hp: 0)).Parse() with
            {
                InBattle = sequence == 3,
                Faints = sequence == 3 ? [trace] : null,
            };
            var observed = await hub.InvokeAsync<ObservationResult>("Observe",
                new Observation(Guid.NewGuid(), session.SessionId, sequence, false, snapshot), lifetime.Token);
            if (!observed.Accepted) throw new InvalidDataException("The battle fixture was rejected.");
        }
        var pending = (await hub.InvokeAsync<AutomationRun>("Status", lifetime.Token)).Players
            .Single(p => p.Id == session.PlayerId).Pokemon.Select(m => m.PendingDeath).OfType<PendingDeath>().Single();
        var otherView = await hubs[1].InvokeAsync<AutomationRun>("Status", lifetime.Token);
        if (pending.Battle != trace || otherView.Players.SelectMany(p => p.Pokemon).Any(m => m.PendingDeath is not null))
            throw new InvalidDataException("Pending battle context was not scoped to the affected player.");
        await hub.InvokeAsync("ResolveFaint", pending.PokemonId, pending.IncidentId, true, "Volltreffer", lifetime.Token);
        var resolved = await hub.InvokeAsync<AutomationRun>("Status", lifetime.Token);
        if (!resolved.Encounters.Single().Dead || !resolved.History.Any(e => e.Kind == "death" && e.Message.Contains("Volltreffer")))
            throw new InvalidDataException("The shared link death or reason was not saved.");
        Console.WriteLine("Verified: bilingual catch check, player-scoped arena changes, and battle context and death confirmation cross SignalR.");
    }
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
    if (verifyOnly)
    {
        await hubs[1].StopAsync(lifetime.Token);
        await WaitUntil(async () => (await hubs[0].InvokeAsync<AutomationRun>("Status", lifetime.Token)).Players
            .Single(p => p.Id == sessions[1].PlayerId).Connection == "Offline");
        Console.WriteLine("Verified: closing a player's connection releases its session while the shared server stays available.");
        host.Kill(true);
        await host.WaitForExitAsync(lifetime.Token);
        current = current with
        {
            Frame = ++frame,
            PlaySeconds = frame,
            Pokemon = [.. current.Pokemon, SyntheticPokemon.Raw(301, 25, slot: 2, area: 17)],
        };
        await Send(current);
        await WaitUntil(async () =>
        {
            using var status = JsonDocument.Parse(await http.GetStringAsync(ui + "/api/status", lifetime.Token));
            var local = status.RootElement.GetProperty("run");
            return status.RootElement.GetProperty("registered").GetBoolean() && status.RootElement.GetProperty("isLocalRun").GetBoolean()
                && local.GetProperty("id").GetGuid() != run.Id
                && local.GetProperty("history").EnumerateArray().Any(e => e.GetProperty("kind").GetString() == "caught"
                    && e.GetProperty("message").GetString()!.Contains("Pikachu", StringComparison.Ordinal));
        });
        Console.WriteLine("Verified: the connected run takes priority and the local feed continues after the isolated server stops.");
        return;
    }
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
    if (verifyOnly) Directory.Delete(data, recursive: true);
}

Process Start(string folder, string dll, params string[] arguments)
{
    var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, folder), CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    string framework = folder == "PokeSoulLinkBot" ? "net10.0" : "net9.0";
    info.ArgumentList.Add(Path.Combine(root, folder, "bin", "Debug", framework, dll));
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
