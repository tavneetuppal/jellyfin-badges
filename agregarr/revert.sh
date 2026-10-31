#!/bin/sh
# Stops any running Agregarr overlay sync, turns overlays off and puts the original (badge-free) posters back.
#   LIBS empty -> every library that still has overlays enabled (libraries already handed to Kometa have none, so
#                 they're skipped and Kometa's posters are left alone)
#   LIBS="Movies|TV" -> just those
# Uses Agregarr's own "reset library posters" job. Needs CONFIG and URL. Runs inside alpine (see install-unraid.sh).
set -eu
command -v jq >/dev/null && command -v curl >/dev/null || apk add -q curl jq >/dev/null

KEY=$(jq -r '.main.apiKey // empty' "$CONFIG/settings.json")
[ -n "$KEY" ] || { echo "No API key in $CONFIG/settings.json"; exit 1; }
H="X-API-Key: $KEY"
post() { curl -s -o /tmp/resp.json -w '%{http_code}' -X POST -H "$H" -H 'Content-Type: application/json' "$@"; }

# 1. Stop everything that's running
post "$URL/api/v1/settings/jobs/overlay-application/cancel" >/dev/null || true
post "$URL/api/v1/settings/jobs/overlay-quick-sync/cancel" >/dev/null || true
curl -sf -H "$H" "$URL/api/v1/overlay-library-configs/status/all" > /tmp/status.json || { echo "Can't reach Agregarr at $URL"; exit 1; }
for id in $(jq -r '.runningLibraries[]?.libraryId' /tmp/status.json); do post "$URL/api/v1/overlay-library-configs/$id/stop" >/dev/null || true; done
echo "Stopping running overlay syncs..."
while [ "$(curl -s -H "$H" "$URL/api/v1/overlay-library-configs/status/all" | jq '(.runningLibraries | length) + (if .jobStatus.running then 1 else 0 end)')" != 0 ]; do sleep 10; done
echo "Nothing running."

# 2. Pick libraries
curl -sf -H "$H" "$URL/api/v1/overlay-library-configs" > /tmp/configs.json
if [ -n "${LIBS:-}" ]; then
  echo "$LIBS" | tr '|' '\n' | jq -R . | jq -s . > /tmp/names.json
  jq --slurpfile n /tmp/names.json '[.configs[] | select(.libraryName as $l | $n[0] | index($l))]' /tmp/configs.json > /tmp/targets.json
else
  jq '[.configs[] | select(any(.enabledOverlays[]; .enabled))]' /tmp/configs.json > /tmp/targets.json
fi
n=$(jq length /tmp/targets.json)
[ "$n" -gt 0 ] || { echo "No libraries to revert (none have overlays enabled)."; exit 0; }
echo "Reverting: $(jq -r 'map(.libraryName) | join(", ")' /tmp/targets.json)"

i=0
while [ "$i" -lt "$n" ]; do
  jq ".[$i]" /tmp/targets.json > /tmp/cfg.json
  name=$(jq -r .libraryName /tmp/cfg.json); id=$(jq -r .libraryId /tmp/cfg.json)

  # 3. Overlays off (kept in the list, unticked) so no sync puts them back
  jq '{libraryName, mediaType, tmdbLanguage, enabledOverlays: [.enabledOverlays[] | .enabled = false]}' /tmp/cfg.json > /tmp/body.json
  code=$(post --data @/tmp/body.json "$URL/api/v1/overlay-library-configs/$id")
  case "$code" in 2??) ;; *) echo "$name: couldn't turn overlays off (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac

  # 4. Agregarr's reset job: uploads each item's saved base poster
  jq -n --arg id "$id" '{libraryId: $id}' > /tmp/body.json
  while :; do
    code=$(post --data @/tmp/body.json "$URL/api/v1/overlay-settings/reset-library-posters")
    case "$code" in 2??) break ;; 409) sleep 15 ;; *) echo "$name: reset failed to start (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac
  done
  echo "$name: restoring original posters..."
  sleep 5
  while curl -s -H "$H" "$URL/api/v1/overlay-settings/reset-status" > /tmp/rs.json && [ "$(jq -r .running /tmp/rs.json)" = true ]; do
    echo "  $(jq -r '"\(.current)/\(.total) (failed: \(.failed))"' /tmp/rs.json)"
    sleep 30
  done
  echo "$name: done ($(jq -r '"\(.total) items, \(.failed) failed"' /tmp/rs.json))"
  i=$((i + 1))
done
echo "All done. Agregarr has no overlays enabled in these libraries any more."
