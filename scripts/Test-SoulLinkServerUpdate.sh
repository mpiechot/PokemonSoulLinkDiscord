#!/usr/bin/env bash
set -euo pipefail

archive=$(realpath "${1:?Pass the Linux server archive}")
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
test_root=$(mktemp -d)
export SOULLINK_RELEASE_ROOT="$test_root/app"
export SOULLINK_STATE_ROOT="$test_root/state"
export SOULLINK_SERVICE_USER
SOULLINK_SERVICE_USER=$(id -un)
export SOULLINK_TEST_PID="$test_root/server.pid"
export SOULLINK_TEST_LOG="$test_root/server.log"
export SOULLINK_TEST_KEY=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
export SOULLINK_TEST_URL
port=$(python3 - <<'PY'
import socket
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
)
SOULLINK_TEST_URL="http://127.0.0.1:$port"
export SOULLINK_HEALTH_URL="$SOULLINK_TEST_URL/health/soullink"

mkdir "$test_root/bin"
export PATH="$test_root/bin:$PATH"
cat >"$test_root/bin/systemctl" <<'SH'
#!/usr/bin/env bash
set -eu
case "$1" in
    is-active) [[ -f $SOULLINK_TEST_PID ]] && kill -0 "$(cat "$SOULLINK_TEST_PID")" ;;
    start)
        cd "$SOULLINK_STATE_ROOT"
        "$SOULLINK_RELEASE_ROOT/current/PokeSoulLinkBot" \
            "--SoulLink:ListenUrl=$SOULLINK_TEST_URL" \
            "--SoulLink:DatabasePath=$SOULLINK_STATE_ROOT/soullink.sqlite" \
            "--SoulLink:AdminKey=$SOULLINK_TEST_KEY" "--SoulLink:EnableDiscordEvents=false" 9>&- >>"$SOULLINK_TEST_LOG" 2>&1 &
        printf '%s\n' "$!" >"$SOULLINK_TEST_PID"
        ;;
    stop)
        if [[ -f $SOULLINK_TEST_PID ]]; then
            kill "$(cat "$SOULLINK_TEST_PID")"
            for ((attempt = 0; attempt < 50; attempt++)); do
                if ! curl --fail --silent --max-time 1 "$SOULLINK_HEALTH_URL" >/dev/null; then
                    rm -- "$SOULLINK_TEST_PID"
                    exit 0
                fi
                sleep 0.1
            done
            exit 1
        fi
        ;;
    *) exit 1 ;;
esac
SH
chmod +x "$test_root/bin/systemctl"

cleanup() {
    local result=$?
    systemctl stop soullink.service || true
    if ((result != 0)) && [[ -f $SOULLINK_TEST_LOG ]]; then tail -n 50 "$SOULLINK_TEST_LOG"; fi
    rm -rf -- "$test_root"
}
trap cleanup EXIT

bash "$script_dir/Update-SoulLinkServer.sh" "$archive"
prior=$(readlink "$SOULLINK_RELEASE_ROOT/current")
api="$SOULLINK_TEST_URL/api/soullink/admin"
auth="X-SoulLink-Admin: $SOULLINK_TEST_KEY"
status=$(curl --silent --output /dev/null --write-out '%{http_code}' "$api/groups")
test "$status" = 401
group=$(curl --fail --silent -H "$auth" -H 'Content-Type: application/json' \
    -d '{"name":"Upgrade-Testgruppe"}' "$api/groups" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
curl --fail --silent -H "$auth" -H 'Content-Type: application/json' \
    -d '{"name":"Run vor dem Update"}' "$api/groups/$group/runs" >/dev/null

bash "$script_dir/Update-SoulLinkServer.sh" "$archive"
test "$(readlink "$SOULLINK_RELEASE_ROOT/current")" != "$prior"
backup=$(find "$SOULLINK_STATE_ROOT/backups" -name 'soullink.sqlite' -type f)
test -f "$backup"
curl --fail --silent -H "$auth" "$api/groups/$group/runs" |
    python3 -c 'import json,sys; assert [r["name"] for r in json.load(sys.stdin)] == ["Run vor dem Update"]'
python3 - "$backup" <<'PY'
import sqlite3
import sys
with sqlite3.connect(sys.argv[1]) as backup:
    assert backup.execute("select name from run_groups").fetchone() == ("Upgrade-Testgruppe",)
PY
printf 'Verified native Linux launch, admin isolation, SQLite backup, update and persisted group data.\n'
