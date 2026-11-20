# Read-only protocol v1

## Lua → local client

TCP JSON Lines on loopback only, default port 5091. The listener accumulates bytes across TCP reads, splits at LF, accepts CRLF, checks UTF-8 and rejects unterminated or oversized messages before unbounded allocation. Maximum frame: 160,000 bytes. Only one local bridge is processed at a time.

A `BridgeFrame` contains `version`, `type: snapshot`, `gameCode: AXVD`, `layoutId`, `epoch`, `frame`, `playSeconds`, map group/number, battle state, `completeInventory: true` and raw occupied party/box slots. Optional fields carry the badge mask, next gym's levels and Ruby type table. Only the types for observed Pokémon flow into parsed inventory. Each slot specifies container, slot, optional box, and the exact 100-/80-byte hex record. Every snapshot scans all fourteen boxes. A partial or checksum-invalid snapshot is discarded as a whole.

Players send their chosen name and marker when joining. Older join requests default to the circle marker. Run status includes each player's last observed area, while omitting raw snapshots. `HeartbeatWithBridge` distinguishes a connected client without fresh emulator data from a live location. The original map is decoded locally in the player without sending ROM graphics or ROM bytes to the server.

There is no incoming Lua command, code-evaluation facility, game write, save manipulation or input simulation.

## Client → server

`POST /api/soullink/join` exchanges a lobby invitation plus display name, `ClientId`, client-generated 256-bit token, ROM capability and protocol version for a registration. The server stores only SHA-256 of the token. Repeating the same client/token registration is idempotent. The local client saves its generated identity before joining, allowing retry if the response is lost.

SignalR endpoint: `/soullink`. The .NET client uses WebSockets directly and sends `Authorization: Bearer …` as a header. Tokens are never placed in URLs. HTTPS/WSS is required for remote deployments. The local diagnostic UI and Lua listener bind only to loopback.

Allowlisted methods:

| Method | Contract |
| --- | --- |
| `Hello(Hello)` | Checks client ID and version; returns new session ID and next persistent sequence. Supersedes any old session for that player. |
| `Observe(Observation)` | Event ID, session ID, sequence, baseline marker and validated complete game snapshot. Returns accepted/state/message/next sequence. |
| `Heartbeat(sessionId)` | Every two seconds from the client; liveness independent of meaningful state changes. |
| `Status()` | Only the authenticated registration's run. |

The authenticated registration, never a payload's player or run ID, determines ownership. Wrong versions, unsupported ROM, duplicate identities/slots, invalid ranges, unknown sessions and large payloads fail at the boundary. Observations are limited to at most one per 150 ms per transport connection; the normal client emits meaningful changes every two seconds. Registration and connection endpoints have a bounded request limiter.

## Sessions and recovery

`Connecting → Synchronizing → Ready`, or `OutOfSync`. Liveness reports Stale after 15 seconds and Offline after 45 seconds. Sessions are in memory; persistent inventories, sequences and receipts survive restart.

Every server/client/Lua reconnect requires a full baseline. Existing inventory is compared by identity, species, experience and egg state. No offline events are replayed. A mismatch, a backward clock/frame/experience, an unexpected epoch, a disappeared identity or a sequence gap pauses automated changes. OutOfSync remains sticky across reconnect until an administrator authorizes rebaseline. Explicit rebaseline imports current state without reconstructing catches, reviving members or replacing links.

Normal snapshots include fresh time/frame for rollback detection, but their wire transmission is triggered by meaningful inventory/map/battle changes. HP-zero observations are repeated to support the two-observation configurable death rule.

## Optional administration API

Disabled unless `SoulLink:AdminKey` contains at least 32 characters. Send it only in `X-SoulLink-Admin`. It permits guild-scoped run creation, lifecycle operations and explicit player rebaseline. Intended for isolated demos/operations; normal administration uses permission-checked Discord commands. Do not expose it publicly without TLS and an operator-controlled key.
