# SoulLink protocol v1

## Lua → local client

TCP JSON Lines on loopback only, default port 5091. The listener accumulates bytes across TCP reads, splits at LF, accepts CRLF, checks UTF-8 and rejects unterminated or oversized messages before unbounded allocation. Maximum frame: 160,000 bytes. Only one local bridge is processed at a time.

A `BridgeFrame` contains `version`, `type: snapshot`, `gameCode`, `layoutId`, `epoch`, `frame`, `playSeconds`, and the game profile's payload. The local client selects the decoder by `gameCode`. For Rubin (`AXVD`), the payload includes map group/number, battle state, `completeInventory: true` and raw occupied party/box slots. Optional fields carry the live map tile, the player's last faint context, badge mask, gym levels and Ruby type table. Each slot specifies container, slot, optional box and the exact 100-/80-byte hex record. Every Rubin snapshot scans all fourteen boxes. A partial or checksum-invalid snapshot is discarded as a whole.

Players send their chosen name and marker when joining. Older join requests default to the circle marker. Run status includes each player's last observed area, while omitting raw snapshots. `HeartbeatWithBridge` distinguishes a connected client without fresh emulator data from a live location. The original map is decoded locally in the player without sending ROM graphics or ROM bytes to the server.

### Item grants on the local bridge

The desktop player enables this optional channel on its own emulator launch. A local click sends the destination (`PC` or `BAG`), one allowed item ID, an expiring request ID, and the current bridge session, trainer and epoch. Lua confirms these values and the loaded Ruby profile before adding 99 items to storage or the matching bag pocket. It returns the new total at that destination or a rejection. The server has no route to this channel.

### Overworld avatars on the local bridge

The owned emulator sends small `type: overworld` frames with the current bridge session, epoch and a nullable pose. The client returns an authenticated `AVATARS` line containing up to eleven nearby players. The bridge checks the local session and epoch before processing the line. Poses carry numeric map, pixel, animation and depth information. Each emulator reads the sprite graphics from its local cartridge.

The client adds each partner's color and truncated name from the authenticated run view to the local `AVATARS` line. Their optional suffix is a six-digit RGB color and a hex-encoded label of at most twelve glyphs. The Lua bridge also accepts pose-only lines.

`AVATARSTYLE <session> <epoch> <rgb|-> <labels>` supplies the local trainer's color and the local visibility preference for other trainers' nameplates. The last field is `1` to show nameplates or `0` to hide them. A dash restores the original local trainer palette. This message stays on the owned loopback bridge.

## Client → server

`POST /api/soullink/join` exchanges a lobby invitation plus display name, `ClientId`, client-generated 256-bit token, ROM capability and protocol version for a registration. The server stores only SHA-256 of the token. Repeating the same client/token registration is idempotent. The local client saves its generated identity before joining, allowing retry if the response is lost.

SignalR endpoint: `/soullink`. The .NET client uses WebSockets directly and sends `Authorization: Bearer …` as a header. Tokens are never placed in URLs. HTTPS/WSS is required for remote deployments. The local diagnostic UI and Lua listener bind only to loopback.

Allowlisted methods:

| Method | Contract |
| --- | --- |
| `Hello(Hello)` | Checks client ID and version; returns new session ID and next persistent sequence. Supersedes any old session for that player. |
| `Observe(Observation)` | Event ID, session ID, sequence, baseline marker and validated complete game snapshot. Returns accepted/state/message/next sequence. |
| `ExchangeOverworld(pose)` | Updates the current session's transient pose and returns other live participants on its map. A null pose removes the sender's avatar. |
| `UpdateAppearance(marker, color)` | Updates the authenticated player's map icon and optional 24-bit RGB color. Null selects the automatic color. |
| `UpdatePlayerProfile(name, marker, color)` | Updates the authenticated player's display name, icon and color together. Names are unique within the run, ignoring case. Returns null on success or a validation message. |
| `HeartbeatWithBridge(sessionId, available)` | Liveness and bridge freshness, independent of meaningful state changes. |
| `Status()` | Only the authenticated registration's run. |
| `CheckCatch(name)` | German or English species name, and the matching caught family in the caller's run if present. |
| `AddRule(text)` | Adds one trimmed line of up to 240 characters to the authenticated caller's run. Status shares the rules with participants. |
| `ChangeArena(number, complete)` | Complete the next arena or revert the most recent completion for the caller. |
| `ResolveFaint(pokemonId, incidentId, confirm, reason)` | Confirm or dismiss the caller's pending knockout. A confirmed death applies to every member of its link. |

The authenticated registration, never a payload's player or run ID, determines ownership. Wrong versions, unsupported ROM, duplicate identities/slots, invalid ranges, unknown sessions and large payloads fail at the boundary. Observations are limited to at most one per 150 ms per transport connection. The client checks for changes once per second and records changed map tiles and catch markers in the run database. Registration and connection endpoints have a bounded request limiter.

## Sessions and recovery

`Connecting → Synchronizing → Ready`, or `OutOfSync`. Liveness reports Stale after 15 seconds and Offline after 45 seconds. Sessions are in memory; persistent inventories, sequences and receipts survive restart.

Every server/client reconnect requires a full baseline. A sequence gap triggers an automatic reconnect. A full snapshot from the same trainer updates the visible inventory and preserves tracked identities, deaths and links. A continuous game can report newly observed catches on reconnect. Following a detected rewind, reset or explicit rebaseline, the new inventory is imported without inferring past catches or reviving members. A different trainer ID is rejected until the correct save returns or the host approves a switch.

The optional `wildBattles` list carries up to sixteen completed wild battles from the current emulator epoch, including the map and observed start/end frames. The server consumes the first completed result per player and area after the run begins. A confirmed catch leaves link creation to inventory tracking. A missed catch marks the encounter failed and marks any existing members dead. Baselines after a timeline change or a paused run discard older battle results.

Normal snapshots include fresh time/frame for rollback detection, but their wire transmission is triggered by meaningful inventory/map/battle changes. HP-zero observations are repeated to support the two-observation configurable death rule.

The desktop client exchanges overworld poses about fifteen times per second while the bridge provides fresh field data. The server rate-limits calls per connection and scopes them to the current authenticated session, run and map. Poses expire after 1.2 seconds and live only in memory. The exchange is independent of inventory sequences, run-change notifications and SQLite.

### Pokémon trades

`OfferTrade` addresses a player and their living Pokémon in the caller's completed link. Only that player can answer via `RespondToTrade`. The server persists the offer, the preparation records, and each player's receipt in the run transaction. Standard status exposes only the participants' display details. The participant-specific `TradeWork` response delivers the incoming record after both clients have called `PrepareTrade`.

The owned bridge verifies the save's trainer, the original record and both identities before starting Ruby's local trade scene. It checkpoints before starting and after returning to the field, including moving the result back to its box slot when applicable. Replaying the same instruction in the resulting checkpoint recognizes the incoming identity. `ConfirmTrade` completes the server-side ownership and link update only after both clients report their saved result. A snapshot containing a previously traded-away identity requires a newer saved state.

After `Hello`, the client calls `Subscribe` to receive `RunChanged` signals scoped to its authenticated run. Committed changes are coalesced before notification. Each notification requests the caller's current view. A periodic status refresh repairs missed signals and supports older servers. The native bridge reports active battle Pokémon and HP where the profile supports them. These transient details disappear when the runner is stale or leaves the fight.

`PokemonDetails` requests a small cartridge-derived learnset, base stats and evolution list from a ready participant in the same run for an observed species. The server relays the request to the owning player and applies a short timeout. The ROM file and save data stay local.

`AddRule(text)` adds a rule for the caller's run. `ChangeRule(current, replacement)` matches the exact current text and replaces it, or removes it if `replacement` is null. A changed or missing rule fails the request so an older editor cannot overwrite a teammate's change. Both calls require participation in a run that has yet to finish.

`GET /api/soullink/statistics` uses the player's bearer token to resolve the group from their registration, including registrations from finished runs. It returns aggregate-ready run summaries for that group, including Discord archives assigned to it. Arena counts use shared progress for automated runs and distinct reported arena numbers for archives. Unknown archive counts are null. Operators can request the same view from `GET /api/soullink/admin/groups/{id}/statistics`, even before any player has registered in the group.

`GET /api/soullink/timeline` applies the same participant scope and returns one ordered event list per run. The operator route is `GET /api/soullink/admin/groups/{id}/timeline`. The server combines durable replay events with the retained live history and reconstructs archive events from recorded catches, deaths and arenas. Unknown event times are null.

## Optional administration API

Disabled unless `SoulLink:AdminKey` contains at least 32 characters. Send it only in `X-SoulLink-Admin`. It permits guild-scoped run creation, lifecycle operations and explicit player rebaseline. Intended for isolated demos/operations; normal administration uses permission-checked Discord commands. Do not expose it publicly without TLS and an operator-controlled key.
