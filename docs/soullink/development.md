# Development and operations

## Build and verify

Executables require a patched .NET/ASP.NET Core runtime at least 9.0.21. SignalR/SQLite packages are pinned to audited versions, including the native SQLite bundle. Check the dependency graph with `dotnet list PokeSoulLinkBot/PokeSoulLinkBot.slnx package --vulnerable --include-transitive`.

```powershell
dotnet restore PokeSoulLinkBot/PokeSoulLinkBot.slnx
dotnet build PokeSoulLinkBot/PokeSoulLinkBot.slnx -m:1 -p:UseSharedCompilation=false
dotnet test PokeSoulLinkBot.Tests/PokeSoulLinkBot.Tests.csproj
.\scripts\Start-SoulLinkDemo.ps1 -Verify
```

The fixtures in `SoulLink.Demo/SyntheticPokemon.cs` construct synthetic encrypted Gen III records from public structure definitions. They contain no real ROM/save data. Tests cover all 24 permutations, checksum/length errors, nickname/egg fields, fragmented JSON Lines, complete party/box observations, three-player groups, duplicate receipts, movement/evolution, lifecycle isolation, rollback, reconnect, persistence transactions and configurable death rules. The demo exercises real loopback sockets and SignalR, including the local client.

## Server configuration

Use .NET configuration (environment variables replace `:` with `__`):

| Key | Default / behavior |
| --- | --- |
| `SoulLink:ListenUrl` | `http://127.0.0.1:5080` |
| `SoulLink:DatabasePath` | `Data/soullink.sqlite` below executable directory |
| `SoulLink:PersistencePath` | Existing manual JSON store, separate from automation |
| `SoulLink:Enabled` | true; false blocks automation endpoints |
| `SoulLink:EnableReadTracking` | true; false blocks automatic ingestion |
| `SoulLink:EnableDiscordEvents` | true; false disables Discord startup and notification delivery |
| `SoulLink:AdminKey` | unset; optional administration API then rejects requests |
| `SoulLink:EnableRemoteWrites` | false; setting true is rejected in this read-only build |
| `SoulLink:EnableAutoTeamSync` | false; setting true is rejected |

`GET /health/soullink` checks database access and reports protocol/read-only readiness. Structured Discord delivery logs identify event and run without logging message tokens. Database failures propagate and do not acknowledge observations. Use the existing systemd restart policy for process recovery.

For a Linux server, publish the bot with `dotnet publish PokeSoulLinkBot/PokeSoulLinkBot.csproj -c Release -o artifacts/publish/server`. Put a TLS reverse proxy in front of the loopback listener; proxy `/soullink` with WebSocket upgrade support and `/api/soullink/join`. Keep the optional admin API restricted to the operator. No Lua port is exposed. Back up the database with SQLite's backup mechanism or stop the service and copy the DB together with any WAL files; copying a live main file alone is insufficient.

## Client configuration and packaging

Client command-line options: `--UiPort=5090`, `--BridgePort=5091`, `--SettingsPath=<absolute-path>`. Each concurrent local player needs independent ports and a separate settings file. The default registration is stored under the current user's LocalApplicationData/SoulLink/client.json; it contains a persistent bearer token and must remain private. Unix files are created with owner-only permissions; Windows relies on the user's profile ACL. Never commit, upload or paste that file into a ticket.

To join a different run, stop the client and use a new `SettingsPath`; the old registration stays available for resuming its run. Automatic token rotation and a registration-selection UI are follow-up work.

Publish a Windows build with:

```powershell
dotnet publish SoulLink.Client/SoulLink.Client.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/client
```

Run it from the published directory so `wwwroot` is available. The web UI is strictly a loopback diagnostic surface; it is not a remotely accessible admin website. Origin/Host checks protect its local mutation endpoints. It renders server data through `textContent`, with no dynamic HTML insertion.

## Improvement review and follow-ups

The implementation uses complete snapshots to avoid false release/catch events from partial box reads, atomic persistence plus receipts, a durable Discord outbox, sticky OutOfSync and a real integration demo. [Issue #107](https://github.com/mpiechot/PokemonSoulLinkDiscord/issues/107) tracks client registration selection and token rotation/revocation. Live AXVD fixture validation remains part of [#72](https://github.com/mpiechot/PokemonSoulLinkDiscord/issues/72) and is the release gate for claiming a verified adapter. Game writes and lossy legacy migration are outside this branch's scope.
