# Development and operations

## Build and verify

The server, Windows player, diagnostic client and integration harness target .NET 10 LTS. The Linux release includes its own patched .NET 10 and ASP.NET Core runtime. SignalR/SQLite packages are pinned, including the native SQLite bundle.

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
| `SoulLink:PersistencePath` | Existing legacy JSON archive; strictly read-only in the automation host |
| `SoulLink:Enabled` | true; false blocks automation endpoints |
| `SoulLink:EnableReadTracking` | true; false blocks automatic ingestion |
| `SoulLink:EnableDiscordEvents` | true; false disables Discord startup and notification delivery |
| `SoulLink:AdminKey` | unset; optional administration API then rejects requests |
| `SoulLink:EnableRemoteWrites` | false; setting true is rejected in this read-only build |
| `SoulLink:EnableAutoTeamSync` | false; setting true is rejected |

`GET /health/soullink` checks database access and reports protocol/read-only readiness. Structured Discord delivery logs identify event and run without logging message tokens. Database failures propagate and do not acknowledge observations. Use the existing systemd restart policy for process recovery.

### Linux release and updates

Run `./scripts/Publish-SoulLinkServer.ps1` to produce `artifacts/server/soullink-linux-x64.tar.gz`. The archive contains the application, its runtime, and a `deploy/` directory with the unit, update script, configuration templates and installation guide. See [Self-hosting](self-hosting.md) for a first installation with your own domain, group and run invitation.

Apply each release with its bundled `deploy/Update-SoulLinkServer.sh` and the server archive. The updater checks the archive's launcher, takes an SQLite backup, applies versioned migrations transactionally, switches the release symlink and checks the service. It retains the previous release and the database backup. If startup fails after migration, inspect the service log before choosing a rollback. Restoring the pre-update backup can discard activity recorded after the update.

### Windows player releases

Deploy the server build with the player-update routes once. Then run the following on the Windows release account, with the local SoulLink player closed.

```powershell
$releaseKey = Join-Path $env:LOCALAPPDATA 'SoulLink\ReleaseSigning\own-server.dpapi'
./scripts/Publish-SoulLinkPlayerUpdate.ps1 -Version 1.0.0 `
    -Server https://soullink.example.org -KeyFile $releaseKey -SshHost my-soullink-server
```

Use your own HTTPS origin and SSH alias. The first release creates a Windows account-protected signing key at the configured path. Keep using that account and key for subsequent releases of that installation. Distribute the first player from `artifacts/player/SoulLink.exe` once to bootstrap updates. For each subsequent release, increase the version and run the same command. Without `-SshHost` the command writes the signed archive at `artifacts/server/soullink-player-release.zip` for a manual upload.

The publisher uploads the signed bundle to the server and publishes the manifest after the corresponding package is available. The player feed is stored next to the automation database and has no database migration or service restart for player-only releases. The Windows player verifies package size, SHA-256, signature and version using its embedded public key, then replaces the existing executable after a normal shutdown. This update signature is independent of Windows Authenticode and SmartScreen.

The reverse proxy forwards the authenticated admin and join API, the health route, and the SignalR `/soullink` route with WebSocket upgrades. Keep the admin key with the host. Player invitations contain only the server address and run invitation code.

## Client configuration and packaging

The normal player product is `SoulLink.Player`, a Windows Forms companion that reuses `ClientRuntime` without starting the web host. Publish the self-contained file with `./scripts/Publish-SoulLinkPlayer.ps1` from PowerShell 7. Each local build updates `artifacts/player/SoulLink.exe`. Close the existing player if Windows has the EXE open. Publish staging and package checks use temporary directories that the scripts remove. The desktop client exports its embedded read-only Lua bridge to a stable per-user path and supports host-prepared EXE copies. See the [player guide](player.md). [#110](https://github.com/mpiechot/PokemonSoulLinkDiscord/issues/110) tracks signing and trusted distribution.

Group builds can embed an alternate icon set from `artifacts/player-artwork` using the same PNG filenames as the built-in icons. The optional directory is local to the build machine. The default group build downloads Pokémon and type artwork on demand. The public build uses the embedded default icons and local sprite files.

Player registrations live separately under `LocalApplicationData/SoulLink/Player`; developer/demo registrations do not become the player's default profile. Synthetic frame markers are rejected unless the explicit development-harness option is enabled. A stale local bridge hides its cached party, and the browser diagnostic page clears party/run/online indicators when its API disappears. `node --test SoulLink.Client.Tests/offline-ui.test.cjs` reproduces and guards the reported stale-demo symptom.

### Optional developer diagnostics

Client command-line options: `--UiPort=5090`, `--BridgePort=5091`, `--SettingsPath=<absolute-path>`. Each concurrent local player needs independent ports and a separate settings file. The default registration is stored under the current user's LocalApplicationData/SoulLink/client.json; it contains a persistent bearer token and must remain private. Unix files are created with owner-only permissions; Windows relies on the user's profile ACL. Never commit, upload or paste that file into a ticket.

To join a different run, stop the client and use a new `SettingsPath`; the old registration stays available for resuming its run. Automatic token rotation and a registration-selection UI are follow-up work.

Publish the separate browser-based diagnostic tool, if needed, with:

```powershell
dotnet publish SoulLink.Client/SoulLink.Client.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/client
```

Run it from the published directory so `wwwroot` is available. The web UI is strictly a loopback diagnostic surface; it is not a remotely accessible admin website. Origin/Host checks protect its local mutation endpoints. It renders server data through `textContent`, with no dynamic HTML insertion.

## Improvement review and follow-ups

The implementation uses complete snapshots to avoid false release/catch events from partial box reads, atomic persistence plus receipts, a durable Discord outbox, automatic same-trainer reconciliation and a real integration demo. [Issue #107](https://github.com/mpiechot/PokemonSoulLinkDiscord/issues/107) tracks client registration selection and token rotation/revocation. Live AXVD fixture validation remains part of [#72](https://github.com/mpiechot/PokemonSoulLinkDiscord/issues/72) and is the release gate for claiming a verified adapter. Game writes and lossy legacy migration are outside this branch's scope.
