# Jellyfin Remote Search

A Jellyfin 12.x plugin that adds results from an external music backend to
Jellyfin's own search, below your local results, in every client (web,
Android/iOS, Finamp). Pressing play on a remote result downloads the track into
a music library; from then on it is a normal library track.

The plugin ships no backend. It talks to one over a unix socket with two
endpoints, described in [docs/BACKEND-API.md](docs/BACKEND-API.md).

## How it behaves

- Searching (3+ characters) adds up to 10 remote tracks to the results. Local
  matches always come first.
- The first play of a remote result waits for the download.
- About a minute later the file is a library item; the remote result for it
  disappears, and so do other releases of the same recording (same title,
  artist and length). Live versions, remasters and covers still show.
- Favorites or playlist entries made on a remote result before its download are
  lost when it turns into a library item.
- Results that were never played are removed after 14 days without showing up
  in a search (scheduled task "Remove old remote results").

## Install

1. Dashboard → Plugins → Repositories → add
   `https://niklasschubert.github.io/jellyfin-plugin-remote-search/manifest.json`.
2. Install "Remote Search" from the catalog and restart Jellyfin.
3. Run a backend whose socket Jellyfin can reach (default
   `/run/remote-search/backend.sock`, changeable on the plugin's settings page).

On the first download the plugin creates a music library "Remote Search" for
its download folder (`/config/data/remote-search-music` in Docker) unless a
library already contains it. If a library with that name exists, the folder is
added to it instead. Both the folder and the library name are settings; set
them before the first play. Changing them later leaves the old folder in its
library.

[`examples/compose.yaml`](examples/compose.yaml) shows the Jellyfin side in
Docker.

- The Jellyfin user must be able to connect to the socket. If the plugin logs
  `backend is not reachable`, check the socket's owner and mode inside the
  Jellyfin container.
- Remote results live in a hidden channel; users whose policy doesn't allow all
  channels may not see them.
- To update by hand instead of through the catalog: stop Jellyfin, replace the
  DLL in `<config>/plugins/RemoteSearch_<version>/`, start Jellyfin. Replacing
  the DLL under a running server crashes its shutdown.

## Development

```sh
nix develop -c dotnet test tests/Jellyfin.Plugin.RemoteSearch.Tests
dev/deploy.sh                       # build, start fake backend + Jellyfin 12.2 on :18096
dev/setup.sh                        # wizard, local Music library; prints a token
dev/check.sh <token> [term]         # search order, play = download, promotion
```

`dev/fake-backend.py` implements the backend API with generated tracks and
sine-tone Ogg files.

## Releases

Push a tag `vX.Y.Z`. CI builds the plugin with
[jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), attaches
the zip to a GitHub release and adds the version to the manifest on GitHub
Pages. Metadata and changelog live in [`build.yaml`](build.yaml).
