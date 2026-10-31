#!/bin/sh
# Turns the Badges_ templates on in every Agregarr MOVIE library and turns off the old templates they replace.
# Other overlays you have enabled are left as they are. Preview by default; APPLY=1 saves.
# Needs CONFIG (Agregarr config folder, for the API key) and URL. Runs inside alpine (see install-unraid.sh).
set -eu
command -v jq >/dev/null && command -v curl >/dev/null || apk add -q curl jq >/dev/null

KEY=$(jq -r '.main.apiKey // empty' "$CONFIG/settings.json")
[ -n "$KEY" ] || { echo "No API key in $CONFIG/settings.json"; exit 1; }
get() { curl -sf -H "X-API-Key: $KEY" "$URL/api/v1/$1"; }

# Old templates the Badges_ set replaces (turned off, not deleted).
OLD='["4K","BluRay","DoVi Top Left","HDR10 Top Left","IMAX","Dolby Atmos","DTS HD MA","DTS X","IMDb Rating","IMDb Rating (Fixed)","Rotten Tomatoes Rating"]'
# Display order of the Badges_ templates in the library list (they don't overlap, so this is only tidiness).
ORDER='["4K","BluRay","1080p","720p","SD","DoVi","HDR10","IMAX","Edition","Dolby Atmos","DTS","Audio","IMDb","Rotten"]'

get overlay-templates > /tmp/templates.json || { echo "Can't reach Agregarr at $URL"; exit 1; }
get overlay-library-configs > /tmp/configs.json

jq -n --slurpfile t /tmp/templates.json --slurpfile c /tmp/configs.json --argjson old "$OLD" --argjson order "$ORDER" '
  ($t[0].templates) as $tpl
  | ([$tpl[] | select(.name | startswith("Badges_"))]
     | sort_by(.name as $n | ([$order | to_entries[] | select(.value as $p | $n | ltrimstr("Badges_") | startswith($p)) | .key] | first // 99), .name)
     | map(.id)) as $badges
  | ([$tpl[] | select(.name as $n | $old | index($n)) | .id]) as $oldIds
  | ($tpl | map({key: (.id | tostring), value: .name}) | from_entries) as $names
  | [$c[0].configs[] | select(.mediaType == "movie") | . as $cfg
     | ([$cfg.enabledOverlays[] | select(.templateId as $id | $badges | index($id) | not)
         | if (.templateId as $id | $oldIds | index($id)) then .enabled = false else . end]
        + [$badges[] | {templateId: ., enabled: true}]
        | to_entries | map(.value + {layerOrder: .key})) as $new
     | {libraryId: $cfg.libraryId,
        body: {libraryName: $cfg.libraryName, mediaType: $cfg.mediaType, enabledOverlays: $new, tmdbLanguage: $cfg.tmdbLanguage},
        turnedOff: [$cfg.enabledOverlays[] | select(.enabled and (.templateId as $id | $oldIds | index($id))) | $names[.templateId | tostring]],
        badges: ($badges | length)}]' > /tmp/plan.json

n=$(jq length /tmp/plan.json)
[ "$n" -gt 0 ] || { echo "No movie libraries set up in Agregarr yet. Open each one under Overlays, save once, then re-run."; exit 1; }

i=0
while [ "$i" -lt "$n" ]; do
  jq ".[$i]" /tmp/plan.json > /tmp/lib.json
  name=$(jq -r .body.libraryName /tmp/lib.json)
  echo "$name: enable $(jq -r .badges /tmp/lib.json) Badges_ templates; turn off: $(jq -r '.turnedOff | if length == 0 then "nothing" else join(", ") end' /tmp/lib.json)"
  if [ "${APPLY:-0}" = 1 ]; then
    jq .body /tmp/lib.json > /tmp/body.json
    code=$(curl -s -o /tmp/resp.json -w '%{http_code}' -X POST -H "X-API-Key: $KEY" -H 'Content-Type: application/json' \
      --data @/tmp/body.json "$URL/api/v1/overlay-library-configs/$(jq -r .libraryId /tmp/lib.json)")
    case "$code" in 2??) echo "  saved" ;; *) echo "  FAILED (HTTP $code): $(head -c 200 /tmp/resp.json)"; exit 1 ;; esac
  fi
  i=$((i + 1))
done

if [ "${APPLY:-0}" = 1 ]; then
  echo "Done. Run the overlays for each library in Agregarr (or wait for its schedule) to update the posters."
else
  echo "Preview only. Re-run with --apply to save."
fi
