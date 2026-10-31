#!/bin/sh
# Removes Agregarr's collections (before letting Kometa manage collections).
# Preview by default; APPLY=1 deletes. Uses Agregarr's own delete, which removes the Plex collections it created
# (labelled by Agregarr) and their labels. "plex"-type configs (collections that existed in Plex before Agregarr)
# are only unlinked: the Plex collection itself is kept.
#   LIBS empty -> all libraries; LIBS="Movies|TV" -> only collections in those libraries
# Needs CONFIG and URL. Runs inside alpine (see install-unraid.sh).
set -eu
command -v jq >/dev/null && command -v curl >/dev/null || apk add -q curl jq >/dev/null

KEY=$(jq -r '.main.apiKey // empty' "$CONFIG/settings.json")
[ -n "$KEY" ] || { echo "No API key in $CONFIG/settings.json"; exit 1; }
H="X-API-Key: $KEY"

curl -sf -H "$H" "$URL/api/v1/collections" > /tmp/cols.json || { echo "Can't reach Agregarr at $URL"; exit 1; }
if [ -n "${LIBS:-}" ]; then
  echo "$LIBS" | tr '|' '\n' | jq -R . | jq -s . > /tmp/names.json
  jq --slurpfile n /tmp/names.json '[.collectionConfigs[] | select(.libraryName as $l | $n[0] | index($l))]' /tmp/cols.json > /tmp/targets.json
else
  jq '[.collectionConfigs[]]' /tmp/cols.json > /tmp/targets.json
fi

n=$(jq length /tmp/targets.json)
[ "$n" -gt 0 ] || { echo "No Agregarr collections found."; exit 0; }
echo "$n Agregarr collection configs:"
jq -r 'group_by(.libraryName)[] | "  \(.[0].libraryName // "?"): " + (map(.name + (if .type == "plex" then " (existing Plex collection - kept)" else "" end)) | join(", "))' /tmp/targets.json

if [ "${APPLY:-0}" != 1 ]; then
  echo "Preview only. Re-run with --apply to delete them."
  exit 0
fi

# stop a running sync so it doesn't recreate anything mid-way
curl -s -o /dev/null -X POST -H "$H" "$URL/api/v1/collections/sync/cancel" || true
while [ "$(curl -s -H "$H" "$URL/api/v1/collections/sync/status" | jq -r .running)" = true ]; do echo "  waiting for the running collection sync to stop..."; sleep 10; done

deleted=0 failed=0
for id in $(jq -r '.[].id' /tmp/targets.json); do
  name=$(jq -r --arg id "$id" '.[] | select(.id == $id) | .name' /tmp/targets.json)
  code=$(curl -s -o /tmp/resp.json -w '%{http_code}' -X DELETE -H "$H" "$URL/api/v1/collections/$id")
  case "$code" in
    2??) echo "  deleted  $name"; deleted=$((deleted + 1)) ;;
    404) echo "  gone     $name (removed with a linked collection)" ;;
    *)   echo "  FAILED   $name (HTTP $code): $(head -c 200 /tmp/resp.json)"; failed=$((failed + 1)) ;;
  esac
done
echo "Done: $deleted deleted, $failed failed. Agregarr finishes removing the Plex collections in the background; give it a minute, then check Plex."
