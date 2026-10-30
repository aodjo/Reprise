#!/usr/bin/env bash
set -euo pipefail
if [[ "$(id -u)" == 0 ]]; then
 mkdir -p /run/dbus
 dbus-daemon --system --fork
 /usr/lib/systemd/systemd-logind > /tmp/reprise-logind.log 2>&1 &
 exec runuser -u repriseqa -- bash /qa/run.sh
fi
export XDG_RUNTIME_DIR=/tmp/reprise-runtime
export XDG_CONFIG_HOME=/tmp/reprise-config
export XDG_DATA_HOME=/tmp/reprise-data
export XDG_CACHE_HOME=/tmp/reprise-cache
mkdir -p "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME/gnome-shell/extensions/reprise@junx.dev" "$XDG_DATA_HOME/gnome-shell/extensions/parity-qa@reprise.test"
chmod 700 "$XDG_RUNTIME_DIR"
cp /source/* "$XDG_DATA_HOME/gnome-shell/extensions/reprise@junx.dev/"
cp /qa/probe.js "$XDG_DATA_HOME/gnome-shell/extensions/parity-qa@reprise.test/extension.js"
printf '%s\n' '{"uuid":"parity-qa@reprise.test","name":"Reprise motion verification","description":"Task-owned native Clutter verification","shell-version":["46"]}' > "$XDG_DATA_HOME/gnome-shell/extensions/parity-qa@reprise.test/metadata.json"
dbus-run-session -- bash -c '
 gsettings set org.gnome.desktop.interface enable-animations true
 gsettings set org.gnome.shell enabled-extensions "[\"reprise@junx.dev\",\"parity-qa@reprise.test\"]"
 gnome-shell --headless --wayland --no-x11 --virtual-monitor=1280x720 &
 shell_pid=$!
 for attempt in {1..400}; do
  [[ ! -f /tmp/reprise-gnome-result.json ]] || break
  kill -0 "$shell_pid" 2>/dev/null || break
  sleep 0.1
 done
 kill "$shell_pid" 2>/dev/null || true
 wait "$shell_pid" || true
' > /tmp/reprise-shell.log 2>&1 || true
cat /tmp/reprise-shell.log
cat /tmp/reprise-gnome-result.json
