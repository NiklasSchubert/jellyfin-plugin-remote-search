#!/usr/bin/env python3
"""Stands in for a backend (docs/BACKEND-API.md): GET /search and POST /download on a unix socket.

Search returns three deterministic tracks per term; download renders a tagged
Ogg tone with ffmpeg after a short delay.
usage: fake-backend.py <socket-path> [search-latency-seconds]
"""
import hashlib
import json
import os
import socketserver
import subprocess
import sys
import tempfile
import time
from http.server import BaseHTTPRequestHandler
from urllib.parse import parse_qs, urlparse

RESULTS_PER_TERM = 3
TRACK_DURATION_MS = 20_000
DOWNLOAD_LATENCY_S = 2
BASE62 = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"

known_tracks = {}
search_latency_s = 0.0


def track_id(term, index):
    digest = int.from_bytes(hashlib.sha1(f"{term}#{index}".encode()).digest(), "big")
    return "".join(BASE62[(digest >> (6 * i)) % 62] for i in range(22))


def search(term, limit):
    normalized = term.strip().lower()
    tracks = []
    for index in range(1, RESULTS_PER_TERM + 1):
        fake_id = track_id(normalized, index)
        track = {
            "kind": "track",
            "uri": f"fake:track:{fake_id}",
            "name": f"{term} Song {index}",
            "artists": ["Fake Artist"],
            "duration_ms": TRACK_DURATION_MS,
            "album_name": "Fake Album",
        }
        known_tracks[track["uri"]] = track
        tracks.append(track)
    return tracks[:limit] if limit > 0 else tracks


def render_ogg(track):
    with tempfile.NamedTemporaryFile(suffix=".ogg") as output:
        subprocess.run(
            [
                "ffmpeg", "-y", "-v", "error",
                "-f", "lavfi", "-i", f"sine=frequency=440:duration={TRACK_DURATION_MS // 1000}",
                "-c:a", "libvorbis",
                "-metadata", f"title={track['name']}",
                "-metadata", f"artist={track['artists'][0]}",
                "-metadata", f"album_artist={track['artists'][0]}",
                "-metadata", f"album={track['album_name']}",
                "-f", "ogg", output.name,
            ],
            check=True,
        )
        return output.read()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        if url.path != "/search":
            return self.reply_json(404, {"error": "not found"})
        query = parse_qs(url.query)
        time.sleep(search_latency_s)
        term = query.get("q", [""])[0]
        limit = int(query.get("limit", ["20"])[0])
        self.reply_json(200, search(term, limit))

    def do_POST(self):
        if self.path != "/download":
            return self.reply_json(404, {"error": "not found"})
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        track = known_tracks.get(body.get("uri"))
        if track is None:
            return self.reply_json(400, {"error": "unknown track"})
        time.sleep(DOWNLOAD_LATENCY_S)
        audio = render_ogg(track)
        self.send_response(200)
        self.send_header("Content-Type", "audio/ogg")
        self.send_header("Content-Length", str(len(audio)))
        self.end_headers()
        self.wfile.write(audio)

    def reply_json(self, status, payload):
        data = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    # unix socket peers have no address, which the default formatter expects
    def log_message(self, format, *args):
        sys.stderr.write("fake-backend: " + (format % args) + "\n")


class UnixHTTPServer(socketserver.ThreadingUnixStreamServer):
    daemon_threads = True

    def get_request(self):
        request, _ = super().get_request()
        return request, ("unix", 0)


def main():
    global search_latency_s
    socket_path = sys.argv[1]
    if len(sys.argv) > 2:
        search_latency_s = float(sys.argv[2])
    if os.path.exists(socket_path):
        os.unlink(socket_path)
    os.makedirs(os.path.dirname(socket_path), exist_ok=True)
    with UnixHTTPServer(socket_path, Handler) as server:
        os.chmod(socket_path, 0o666)
        print(f"control socket: {socket_path}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
