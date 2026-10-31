#!/bin/sh
# Hands libraries over from Agregarr to Kometa:
#   1. puts every poster Agregarr overlaid back to its clean original
#      (Agregarr only does this on a run where nothing matches, so a match-nothing template is enabled for one run)
#   2. waits for that run to finish
#   3. leaves the library configured with nothing enabled, so Agregarr skips it from then on
# Needs CONFIG, URL and LIBS ("Movies 4K/HDR|TV 4K/HDR"). Runs inside alpine (see install-unraid.sh).
set -eu
command -v jq >/dev/null && command -v curl >/dev/null || apk add -q curl jq >/dev/null

KEY=$(jq -r '.main.apiKey // empty' "$CONFIG/settings.json")
[ -n "$KEY" ] || { echo "No API key in $CONFIG/settings.json"; exit 1; }
H="X-API-Key: $KEY"
api() { curl -s -o /tmp/resp.json -w '%{http_code}' -H "$H" -H 'Content-Type: application/json' "$@"; }

# 1. A template that can never match (only used for the reset run)
RESET="Badges_Reset (matches nothing)"
curl -sf -H "$H" "$URL/api/v1/overlay-templates" > /tmp/templates.json || { echo "Can't reach Agregarr at $URL"; exit 1; }
RID=$(jq -r --arg n "$RESET" '[.templates[] | select(.name == $n)][0].id // empty' /tmp/templates.json)
if [ -z "$RID" ]; then
  jq -n --arg n "$RESET" '{name: $n, description: "Used once to put clean posters back before handing a library to Kometa", type: "generic",
    templateData: {width: 1000, height: 1500, elements: [{id: "none", layerOrder: 0, type: "tile", x: 0, y: 0, width: 1, height: 1,
      properties: {fillColor: "#000000", fillOpacity: 0}}]},
    applicationCondition: {sections: [{rules: [{field: "filePath", operator: "contains", value: "__badges_reset_never_matches__"}]}]}}' > /tmp/body.json
  code=$(api -X POST --data @/tmp/body.json "$URL/api/v1/overlay-templates")
  case "$code" in 2??) RID=$(jq -r .id /tmp/resp.json) ;; *) echo "Couldn't create the reset template (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac
fi

curl -sf -H "$H" "$URL/api/v1/overlay-library-configs" > /tmp/configs.json
echo "$LIBS" | tr '|' '\n' | while IFS= read -r LIB; do
  [ -n "$LIB" ] || continue
  jq --arg n "$LIB" '[.configs[] | select(.libraryName == $n)][0] // empty' /tmp/configs.json > /tmp/cfg.json
  if [ ! -s /tmp/cfg.json ]; then echo "$LIB: not found in Agregarr (check the exact name)"; continue; fi
  ID=$(jq -r .libraryId /tmp/cfg.json)

  # everything off, reset template on -> every previously overlaid item has no match -> clean original restored
  jq --argjson r "$RID" '{libraryName, mediaType, tmdbLanguage,
      enabledOverlays: ([.enabledOverlays[] | select(.templateId != $r) | .enabled = false] + [{templateId: $r, enabled: true}]
                        | to_entries | map(.value + {layerOrder: .key}))}' /tmp/cfg.json > /tmp/body.json
  code=$(api -X POST --data @/tmp/body.json "$URL/api/v1/overlay-library-configs/$ID")
  case "$code" in 2??) ;; *) echo "$LIB: couldn't save (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac

  echo "$LIB: restoring clean posters..."
  while :; do
    code=$(api -X POST "$URL/api/v1/overlay-library-configs/$ID/apply")
    case "$code" in 2??) break ;; 409) sleep 30 ;; *) echo "$LIB: couldn't start (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac
  done
  sleep 10
  while [ "$(curl -s -H "$H" "$URL/api/v1/overlay-library-configs/$ID/status" | jq -r '.running')" = true ]; do
    echo "  $(curl -s -H "$H" "$URL/api/v1/overlay-library-configs/$ID/status" | jq -r '"\(.progressPercent // "?")%"')"
    sleep 30
  done

  # nothing enabled -> Agregarr skips this library from now on (the templates stay in the list, unticked)
  jq --argjson r "$RID" '{libraryName, mediaType, tmdbLanguage,
      enabledOverlays: ([.enabledOverlays[] | select(.templateId != $r) | .enabled = false] | to_entries | map(.value + {layerOrder: .key}))}' \
      /tmp/cfg.json > /tmp/body.json
  code=$(api -X POST --data @/tmp/body.json "$URL/api/v1/overlay-library-configs/$ID")
  case "$code" in 2??) echo "$LIB: clean posters restored; Agregarr no longer touches this library" ;; *) echo "$LIB: couldn't save (HTTP $code)"; exit 1 ;; esac
done
