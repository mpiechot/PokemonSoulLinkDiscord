# Architecture

The existing executable co-hosts the Discord lifecycle and ASP.NET Core SignalR. `BotHost.CreateBuilder` remains available for composition tests; `RunAsync` creates the web host using the same service registrations.

- `SoulLink`: transport DTOs, Gen III parsing, map/species catalogs, transactional SQLite store, lifecycle and observation engine, bounded JSON Lines framing and shared status presentation.
- `PokeSoulLinkBot/Automation`: SignalR authorization boundary, optional administration API, Discord commands/context routing, asynchronous durable notification dispatcher.
- `SoulLink.Client`: loopback bridge listener, raw-frame validation, authenticated SignalR session and local diagnostic UI. It has no encounter business rules and no game-write API.
- `integration/mgba/soullink.lua`: reads fixed, documented Ruby reference structures once per 60 emulated frames and emits complete inventories. The only outbound socket connects to `127.0.0.1`.
- `SoulLink.Demo`: synthetic Pokémon generator and a real three-player integration harness, with isolated server/client processes and databases.

## Consistency

Each SQLite run is stored independently and queried by ID/guild. Its document contains frozen membership, observed Pokémon and encounters. Registration hashes are separately indexed. Accepted observations commit run state, event receipts and notification outbox entries in one SQLite transaction. The store serializes transactions within the process, and SQLite enforces unique selected contexts and receipts. This initial implementation favors simple ordering over per-run concurrent writes.

Persistent IDs use server GUIDs. Game identity is the original trainer's full 32-bit ID plus 32-bit PID; species, nickname, position and evolution do not contribute. An exact cloned identity cannot be distinguished and is rejected when seen twice in one inventory.

The initial full inventory is a baseline, including preexisting Pokémon. Subsequent new identities can fill one encounter slot per player/region section. A group is complete only after every frozen participant has a member. The completed membership is not replaced by extra catches. Eggs are tracked but do not become encounters; hatching an imported egg does not retroactively create a catch.

Party/box state is observed, never forced. An alive completed group is active only when all members are present in party; it is boxed only when all are present in boxes. Mixed or missing membership is diagnosed.

## Compatibility with manual runs

Legacy JSON data and its manual model remain intact. There is no lossy automatic conversion to the new identity-based model. Shared status/team/lifecycle commands route through `AutomationContextCommand` when an automated context is selected. Manual mutations cannot accidentally target a legacy run while an automated run is selected. Pure information commands remain available.

## Delivery semantics

Processing is idempotent for `(player, EventId, Sequence)`. The notification outbox prevents duplicate processing from enqueuing new Discord messages. Discord delivery is **at least once**: a process crash after Discord accepts a message and before the outbox acknowledgement can cause a duplicate after restart. This is documented rather than claiming cross-system exactly-once delivery.

History shown in the run is capped at 200 entries. Receipts and pending outbox items remain durable. Database retention/archival is a future maintenance concern.
