#!/usr/bin/env bash
# Completes the startup wizard, creates a local Music library and prints an access token.
set -euo pipefail

readonly BASE_URL=${BASE_URL:-http://localhost:18096}
readonly ADMIN_USER=dev
readonly ADMIN_PASSWORD=dev
readonly CLIENT_HEADER='MediaBrowser Client="dev", Device="cli", DeviceId="dev-cli", Version="0.0.1"'

json_post() {
  curl -sf -m 30 -X POST -H 'Content-Type: application/json' "$@"
}

until curl -sf -m 5 "$BASE_URL/Startup/Configuration" >/dev/null; do sleep 2; done

json_post "$BASE_URL/Startup/Configuration" -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl -sf -m 10 "$BASE_URL/Startup/User" >/dev/null
json_post "$BASE_URL/Startup/User" -d "{\"Name\":\"$ADMIN_USER\",\"Password\":\"$ADMIN_PASSWORD\"}"
json_post "$BASE_URL/Startup/Complete"

token=$(json_post "$BASE_URL/Users/AuthenticateByName" -H "Authorization: $CLIENT_HEADER" \
  -d "{\"Username\":\"$ADMIN_USER\",\"Pw\":\"$ADMIN_PASSWORD\"}" | jq -r .AccessToken)
readonly AUTH="Authorization: $CLIENT_HEADER, Token=\"$token\""

json_post "$BASE_URL/Library/VirtualFolders?name=Music&collectionType=music&refreshLibrary=true" -H "$AUTH" \
  -d '{"LibraryOptions":{"PathInfos":[{"Path":"/music"}]}}'
# the plugin's startup scan can swallow the refresh above; this one restarts the scan with every library
json_post "$BASE_URL/Library/Refresh" -H "$AUTH"

echo "token: $token"
