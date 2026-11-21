# Reliability and recovery

The process has three explicit failure boundaries:

1. Discord client startup failures are logged as fatal, diagnostics are
   recorded when possible, event handlers are detached, and the exception is
   rethrown. This keeps an unrecoverable process failure visible to the host
   supervisor instead of leaving a half-started bot running.
2. Discord log events, command routing, ready-time initialization, and cache
   warmup are isolated task boundaries. A failure is logged and recorded, but
   does not escape into the Discord gateway callback or stop unrelated work.
3. Shutdown attempts client stop and logout independently. A cleanup failure is
   logged and recorded while the remaining shutdown steps still run.

The service file in `deploy/pokesoullinkbot.service` is a deployment recipe for
systemd. `Restart=on-failure` restarts the process after an unrecoverable host
failure; it does not hide the original exception from the logs.

State-changing operations remain behind the application services and their
persistence boundary. A command is acknowledged by the existing router before
its result is reported, and the host does not introduce an in-memory retry that
could apply a state change twice.
