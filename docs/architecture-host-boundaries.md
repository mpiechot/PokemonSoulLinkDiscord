# Host and dependency boundaries

The application is composed through `BotHost`, the single composition root for
the executable. `Program` owns process-level logging and delegates host
lifecycle to this composition root.

## Runtime boundaries

```text
Discord.Net transport
        |
DiscordBotHostedService / slash-command router
        |
Application services and command handlers
        |
Core run model and game rules
        |
Infrastructure persistence and external data adapters
```

- `DiscordBotHostedService` owns Discord client lifecycle and translates
  Discord events into application entry points.
- `Bot.Commands` and `Bot.Handlers` are transport-facing adapters. They call
  application interfaces instead of constructing persistence or HTTP clients.
- `Application` owns use cases such as run and catch handling.
- `Core` owns run state and rules. A run identifier is passed through the
  application services so concurrent run contexts cannot be selected by
  process-global mutable state.
- `Infrastructure` owns JSON persistence and external data access.
- `BotHost` is the only place where these concrete implementations are wired
  together. Tests can build the host without starting Discord connections.

The initial host composition deliberately does not include mGBA integration,
SignalR, RAM writes, or automatic team synchronization. Those capabilities are
separate future adapters and must not leak into the current run services.
