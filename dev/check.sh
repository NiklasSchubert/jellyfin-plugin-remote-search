#!/usr/bin/env bash
# End-to-end against the dev server and fake backend: search, play = download, promotion, dedupe.
# usage: check.sh <token> [term]
set -euo pipefail

readonly BASE_URL=${BASE_URL:-http://localhost:18096}
readonly TOKEN=$1
readonly TERM_QUERY=${2:-fake}
readonly ENCODED_TERM=$(jq -rn --arg term "$TERM_QUERY" '$term | @uri')
readonly AUTH="Authorization: MediaBrowser Token=\"$TOKEN\""
readonly PROMOTION_TIMEOUT_S=180
readonly OUT=$(mktemp)
trap 'rm -f "$OUT"' EXIT

api() {
  curl -sf -m 60 -H "$AUTH" "$BASE_URL$1"
}

# prints the HTTP status of a playback report for an item, as a player sends it every ~10 s
report_playback() {
  local endpoint=$1 item_id=$2
  curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST -H "$AUTH" -H 'Content-Type: application/json' \
    -d "{\"ItemId\":\"$item_id\",\"PositionTicks\":100000000}" "$BASE_URL/Sessions/Playing$endpoint"
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

readonly WEB_GLOBAL_TYPES=Movie,Series,Episode,Playlist,MusicAlbum,Audio,TvChannel,PhotoAlbum,Photo,AudioBook,Book,BoxSet

# what jellyfin-web 12.2's search page sends with no library selected (useSearchItems.ts), songs only
search() {
  api "/Items?userId=$user_id&searchTerm=$ENCODED_TERM&includeItemTypes=$WEB_GLOBAL_TYPES&isMissing=false&recursive=true&limit=800&enableTotalRecordCount=false&fields=ProviderIds,Path" \
    | jq '{Items: [.Items[] | select(.Type == "Audio")]}'
}

# what Finamp sends: the legacy per-user route, scoped to a music library
search_finamp() {
  api "/Users/$user_id/Items?SearchTerm=$ENCODED_TERM&IncludeItemTypes=Audio&ParentId=$music_library_id&Recursive=true&Limit=100" \
    | jq '{Items: [.Items[] | select(.Type == "Audio")]}'
}

assert_local_first_with_remote_hits() {
  local label=$1 results=$2
  echo "== $label"
  echo "$results" | jq -r '.Items[] | "  \(.Name)\t\(if .ChannelId then "remote" else "local" end)"'
  local first_kind remote_hits
  first_kind=$(echo "$results" | jq -r '.Items[0].ChannelId // "local"')
  remote_hits=$(echo "$results" | jq '[.Items[] | select(.ChannelId)] | length')
  [[ $remote_hits -gt 0 ]] || fail "$label: no remote hits"
  [[ $first_kind == local ]] || fail "$label: a remote hit sorts before the local song"
}

user_id=$(api /Users/Me | jq -r .Id)
music_library_id=$(api /Library/VirtualFolders | jq -r '.[] | select(.Name == "Music") | .ItemId')

# the browser keeps an empty answer for a minute, so the very first request has to carry the remote hits
results=$(search)
assert_local_first_with_remote_hits "web global search (first, uncached request)" "$results"
assert_local_first_with_remote_hits "Finamp search" "$(search_finamp)"

placeholder_id=$(echo "$results" | jq -r '[.Items[] | select(.ChannelId)][0].Id')
placeholder_name=$(echo "$results" | jq -r '[.Items[] | select(.ChannelId)][0].Name')

echo "== play $placeholder_name"
status=$(curl -s -m 300 -H "$AUTH" -o "$OUT" -w '%{http_code} %{time_total}s' \
  "$BASE_URL/Audio/$placeholder_id/universal?userId=$user_id&deviceId=dev-check&container=ogg,mp3&audioCodec=mp3&transcodingContainer=mp3&transcodingProtocol=http")
probe=$(ffprobe -v error -show_entries format=format_name,duration -of csv=p=0 "$OUT" 2>&1 | head -1 || true)
echo "  $status -> $probe"
[[ $probe == ogg,* ]] || fail "playback returned no ogg"
start_status=$(report_playback "" "$placeholder_id")
[[ $start_status == 204 ]] || fail "playback start report answered $start_status"

echo "== wait for promotion (library monitor delay)"
for ((waited = 0; waited < PROMOTION_TIMEOUT_S; waited += 5)); do
  promoted=$(search | jq -r --arg name "$placeholder_name" \
    '[.Items[] | select(.Name == $name)] | if length == 1 and (.[0].ChannelId | not) then .[0].ProviderIds.RemoteSearch // "" else "" end')
  [[ -n $promoted ]] && break
  sleep 5
done
[[ -n ${promoted:-} ]] || fail "$placeholder_name not promoted to a single library item within ${PROMOTION_TIMEOUT_S}s"
echo "  library item with ProviderIds.RemoteSearch=$promoted, remote result hidden"

echo "== keep playing the remote result after promotion"
progress_status=$(report_playback /Progress "$placeholder_id")
echo "  progress report: $progress_status"
[[ $progress_status == 204 ]] || fail "progress report for the playing remote result answered $progress_status"

echo "PASS"
