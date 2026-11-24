#!/usr/bin/env bash
set -euo pipefail
umask 027

if [[ $# != 1 || ! -f $1 ]]; then
    printf 'Usage: %s /path/to/soullink-linux-x64.tar.gz\n' "$0" >&2
    exit 2
fi

archive=$(realpath "$1")
root=${SOULLINK_RELEASE_ROOT:-/opt/soullink}
state=${SOULLINK_STATE_ROOT:-/var/lib/soullink}
unit=${SOULLINK_UNIT:-soullink.service}
operator=${SOULLINK_SERVICE_USER:-soullink}
health=${SOULLINK_HEALTH_URL:-http://127.0.0.1:5080/health/soullink}
database="$state/soullink.sqlite"

install -d -m 0755 "$root" "$root/releases"
exec 9>"$root/update.lock"
flock -n 9 || { printf 'A SoulLink update is already running.\n' >&2; exit 1; }

as_service() {
    if [[ $(id -un) == "$operator" ]]; then "$@"; else runuser -u "$operator" -- "$@"; fi
}

if [[ $(id -u) == 0 ]]; then
    install -d -m 0750 -o "$operator" "$state" "$state/backups"
else
    install -d -m 0750 "$state" "$state/backups"
fi

release=$(mktemp -d "$root/releases/$(date -u +%Y%m%dT%H%M%S)-XXXXXX")
tar --no-same-owner --no-same-permissions -xzf "$archive" -C "$release"
test -f "$release/PokeSoulLinkBot"
chmod -R a+rX "$release"
chmod 0755 "$release" "$release/PokeSoulLinkBot"
binary="$release/PokeSoulLinkBot"

# Check the bundle before interrupting the live service.
probe=$(as_service mktemp -d "$state/.probe-XXXXXX")
was_running=false
schema_ready=false
cleanup() {
    local result=$?
    as_service rm -f -- "$probe/check.sqlite" "$probe/check.sqlite-shm" "$probe/check.sqlite-wal"
    as_service rmdir -- "$probe"
    if ((result != 0)) && $was_running && ! $schema_ready; then systemctl start "$unit"; fi
}
trap cleanup EXIT
as_service "$binary" --migrate "$probe/check.sqlite"

previous=$(readlink "$root/current" || true)
backup=''
if systemctl is-active --quiet "$unit"; then
    systemctl stop "$unit"
    was_running=true
fi

if [[ -f $database ]]; then
    backup_dir=$(as_service mktemp -d "$state/backups/$(date -u +%Y%m%dT%H%M%S)-XXXXXX")
    backup="$backup_dir/soullink.sqlite"
    as_service "$binary" --backup "$database" "$backup"
fi

as_service "$binary" --migrate "$database"
schema_ready=true

ln -s "$release" "$root/.current.$$"
mv -Tf -- "$root/.current.$$" "$root/current"
if systemctl start "$unit"; then
    for ((attempt = 0; attempt < 30; attempt++)); do
        if curl --fail --silent --max-time 2 "$health" >/dev/null && systemctl is-active --quiet "$unit"; then
            printf 'SoulLink is ready. Release: %s\n' "$release"
            if [[ -n $backup ]]; then printf 'Database backup: %s\n' "$backup"; fi
            exit 0
        fi
        sleep 1
    done
fi
printf 'Startup check failed. Previous release: %s\n' "${previous:-none}" >&2
printf 'Database backup: %s\n' "${backup:-none}" >&2
printf 'Inspect the service log. The updater has retained the current database and backups.\n' >&2
exit 1
