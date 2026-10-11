#!/usr/bin/env bash
# Builds the plugin, installs it into the dev server, starts the fake backend and (re)starts Jellyfin.
set -euo pipefail
cd "$(dirname "$0")"

readonly PLUGIN_DIR=env/config/plugins/RemoteSearch_0.1.0.0
readonly LOCAL_ALBUM_DIR="env/music/Local Artist/Local Album"
readonly SOCKET=env/run/backend.sock
# real backend searches take ~1.5-3 s
readonly FAKE_SEARCH_LATENCY_S=2.5

nix develop .. -c dotnet build ../src/Jellyfin.Plugin.RemoteSearch -c Release -o out
mkdir -p "$PLUGIN_DIR" env/cache env/run "$LOCAL_ALBUM_DIR"
cp out/Jellyfin.Plugin.RemoteSearch.dll "$PLUGIN_DIR/"

if [[ ! -f "$LOCAL_ALBUM_DIR/01 - Local Fake Tune.ogg" ]]; then
  ffmpeg -v error -f lavfi -i sine=frequency=330:duration=10 -c:a libvorbis \
    -metadata title="Local Fake Tune" -metadata artist="Local Artist" -metadata album="Local Album" \
    "$LOCAL_ALBUM_DIR/01 - Local Fake Tune.ogg"
fi

pkill -f "fake-backend.py" || true
setsid nohup ./fake-backend.py "$SOCKET" "$FAKE_SEARCH_LATENCY_S" >env/fake-backend.log 2>&1 < /dev/null &

JF_UID="$(id -u)" JF_GID="$(id -g)" docker compose up -d --force-recreate
