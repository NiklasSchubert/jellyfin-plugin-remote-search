# Backend API

The plugin talks HTTP/1.1 to a backend over a unix socket (setting
`BackendSocketPath`, default `/run/remote-search/backend.sock`). The Jellyfin
user must be able to connect to it. Anything that serves these two endpoints
works; `dev/fake-backend.py` is a complete example.

Errors: any non-2xx status. A JSON body `{"error": "..."}` is shown in the
Jellyfin log; other bodies are logged as they are.

## `GET /search?type=track&limit=<n>&q=<term>`

Returns a JSON array, best match first, at most `limit` entries:

```json
[
  {
    "kind": "track",
    "uri": "fake:track:42",
    "name": "Song title",
    "artists": ["First artist", "Featured artist"],
    "duration_ms": 213573,
    "album_name": "Album title",
    "cover_url": "https://example.com/cover.jpg"
  }
]
```

| Field | Required | Use |
|---|---|---|
| `kind` | yes | entries other than `"track"` are ignored |
| `uri` | yes | opaque, stable track id; sent back to `/download` and used as the placeholder's identity |
| `name` | yes | title |
| `artists` | no | first entry is the primary artist (folder name, dedupe) |
| `duration_ms` | yes | runtime; dedupe tolerance ±2 s |
| `album_name` | no | folder name |
| `cover_url` | no | placeholder image, loaded by Jellyfin |

Other fields are ignored. The plugin waits `SearchTimeoutMs` (default 6 s) for
an answer, then returns no remote results but keeps the request running for up
to 1 minute to fill its cache. Aim for well under that.

## `POST /download`

Body: `{"uri": "<uri from /search>"}` with a `Content-Length`.

Returns the audio file as the response body: **Ogg Vorbis**, stereo,
44.1 kHz. The plugin saves it as `<Artist>/<Album>/<Title>.ogg` in its download
folder, and Jellyfin builds the library item from the file's tags, so tag it
(`TITLE`, `ARTIST`, `ALBUMARTIST`, `ALBUM`, `TRACKNUMBER`, `DISCNUMBER`,
`DATE`, embedded cover).

The plugin sends one download at a time and gives up after 4 minutes, so the
backend may serialize downloads but should answer within 3.
