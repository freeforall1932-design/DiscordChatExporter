#!/usr/bin/env bash
#
# End-to-end smoke test for the web interface hosted by `DiscordChatExporter.Cli gui`.
#
# Usage: ./scripts/smoke-test-gui.sh [path-to-binary] [port]
#
set -euo pipefail
set -x

BIN="${1:-./publish/DiscordChatExporter.Cli}"
PORT="${2:-5155}"
BASE="http://127.0.0.1:${PORT}"

info() { echo "::notice title=smoke::${*}"; }
fail() { echo "::error title=smoke::${*}"; exit 1; }

info "starting the server on port ${PORT}"
"${BIN}" gui --port "${PORT}" --host any --no-browser > gui.log 2>&1 &
GUI_PID=$!
trap 'kill "${GUI_PID}" 2>/dev/null || true' EXIT

READY=0
for _ in $(seq 1 60); do
    if curl -sf "${BASE}/api/info" -o info.json; then
        READY=1
        break
    fi

    if ! kill -0 "${GUI_PID}" 2>/dev/null; then
        break
    fi

    sleep 0.5
done

if [ "${READY}" != "1" ]; then
    fail "the server did not start: $(tr '\n' ' ' < gui.log | head -c 600)"
fi

info "server output: $(tr '\n' ' ' < gui.log | head -c 300)"

echo "--- /api/info ---"
python3 -m json.tool info.json | head -80

info "checking the command catalog"
python3 - <<'PY'
import json

info = json.load(open("info.json"))
commands = {c["name"]: c for c in info["commands"]}
print("commands:", sorted(commands))

expected = [
    "channels",
    "dm",
    "export",
    "exportall",
    "exportdm",
    "exportguild",
    "guilds",
    "guide",
]
missing = [c for c in expected if c not in commands]
assert not missing, f"missing commands: {missing}"

# The gui command itself must not be exposed as a button
assert "gui" not in commands, "the gui command should not be exposed"

for name, command in commands.items():
    assert command["title"], name
    assert command["group"], name
    for option in command["options"]:
        assert option["kind"], (name, option["name"])
        assert option["label"], (name, option["name"])
        assert option["name"] != "token", "the token option must be handled by the interface"

kinds = {o["name"]: o["kind"] for o in commands["export"]["options"]}
print("export option kinds:", kinds)
assert kinds["channel"] == "list"
assert kinds["format"] == "select"
assert kinds["output"] == "path"
assert kinds["media"] == "bool"
assert kinds["after"] == "date"
assert commands["export"]["requiresToken"] is True

guild = [o for o in commands["channels"]["options"] if o["name"] == "guild"][0]
assert guild["isRequired"] is True, "the guild option should be required"

print("OK")
PY

info "running the guide command through the API"
curl -sf -X POST "${BASE}/api/runs" \
    -H 'Content-Type: application/json' \
    -d '{"command":"guide"}' > run.json
python3 -m json.tool run.json

RUN_ID="$(python3 -c "import json;print(json.load(open('run.json'))['id'])")"

python3 - "${PORT}" "${RUN_ID}" <<'PY'
import json
import sys
import time
import urllib.request

port, run_id = sys.argv[1], sys.argv[2]
cursor = 0
output = ""
state = "running"
payload = {}
deadline = time.time() + 60

while time.time() < deadline:
    url = f"http://127.0.0.1:{port}/api/runs/{run_id}?cursor={cursor}&wait=2000"
    with urllib.request.urlopen(url) as response:
        payload = json.load(response)

    output += payload["output"]
    cursor = payload["cursor"]
    state = payload["state"]

    if state != "running":
        break

print("state:", state, "exit code:", payload.get("exitCode"))
print("command line:", payload.get("commandLine"))
print("output:", repr(output[:300]))

assert state == "succeeded", state
assert "developer tools" in output, output
assert "authorization" in output, output
assert "DiscordChatExporter.Cli" in payload["commandLine"], payload["commandLine"]
print("OK")
PY

info "checking that invalid requests are rejected"
STATUS="$(curl -s -o error.json -w '%{http_code}' -X POST "${BASE}/api/runs" \
    -H 'Content-Type: application/json' \
    -d '{"command":"channels","options":{}}')"
cat error.json
echo

[ "${STATUS}" = "400" ] || fail "expected status 400 for a run without required options, got ${STATUS}"
python3 - <<'PY'
import json

error = json.load(open("error.json"))
print(error)
assert error["details"], error
assert any("required" in d.lower() or "token" in d.lower() for d in error["details"]), error
print("OK")
PY

info "checking that cross-origin requests are rejected"
STATUS="$(curl -s -o /dev/null -w '%{http_code}' -X POST "${BASE}/api/runs" \
    -H 'Content-Type: application/json' \
    -H 'Origin: https://evil.example.com' \
    -d '{"command":"guide"}')"
[ "${STATUS}" = "403" ] || fail "expected status 403 for a cross-origin request, got ${STATUS}"

info "checking that unknown commands are rejected"
STATUS="$(curl -s -o /dev/null -w '%{http_code}' -X POST "${BASE}/api/runs" \
    -H 'Content-Type: application/json' \
    -d '{"command":"nope"}')"
[ "${STATUS}" = "400" ] || fail "expected status 400 for an unknown command, got ${STATUS}"

info "checking the run list"
curl -sf "${BASE}/api/runs" | python3 -m json.tool | head -30

info "running a custom command line"
curl -sf -X POST "${BASE}/api/runs" \
    -H 'Content-Type: application/json' \
    -d '{"rawCommandLine":"guide"}' > raw-run.json

RAW_ID="$(python3 -c "import json;print(json.load(open('raw-run.json'))['id'])")"
python3 - "${PORT}" "${RAW_ID}" <<'PY'
import json
import sys
import time
import urllib.request

port, run_id = sys.argv[1], sys.argv[2]
deadline = time.time() + 60
payload = {}

while time.time() < deadline:
    url = f"http://127.0.0.1:{port}/api/runs/{run_id}?cursor=0&wait=2000"
    with urllib.request.urlopen(url) as response:
        payload = json.load(response)

    if payload["state"] != "running":
        break

print("state:", payload["state"], "output:", repr(payload["output"][:200]))
assert payload["state"] == "succeeded", payload
assert "authorization" in payload["output"], payload
print("OK")
PY

info "checking the assets are served"
curl -sf "${BASE}/" -o index.html
curl -sf "${BASE}/app.js" -o served-app.js
curl -sf "${BASE}/app.css" -o served-app.css
diff -q DiscordChatExporter.Cli/Gui/Assets/app.js served-app.js
diff -q DiscordChatExporter.Cli/Gui/Assets/app.css served-app.css
diff -q DiscordChatExporter.Cli/Gui/Assets/index.html index.html

info "checking the log download endpoint"
curl -sf "${BASE}/api/runs/${RUN_ID}/log" -o run.log
grep -q "authorization" run.log

info "SMOKE TEST PASSED"
