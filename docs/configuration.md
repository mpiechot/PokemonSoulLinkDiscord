# Runtime configuration

The host reads the `SoulLink` section from `appsettings.json`, environment
variables, and optional user secrets. The checked-in defaults are safe for a
fresh installation:

- read-only tracking is enabled;
- Discord event handling is enabled;
- remote game writes are disabled;
- automatic team synchronization is disabled.

The Discord token is supplied through the `DISCORD_BOT_TOKEN` environment
variable or user secrets. It is intentionally not part of `SoulLinkOptions`,
configuration files, diagnostics, or exported run data, and the host never
logs the token value.

`EnableAutoTeamSync` requires `EnableRemoteWrites`; inconsistent combinations
are rejected during host startup. Explicitly enabling remote writes remains a
deliberate operator decision and does not activate an implementation that has
not been added yet.
