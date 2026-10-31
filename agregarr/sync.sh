#!/bin/sh
# Installs/updates the Badges_ overlay templates in Agregarr through its API.
#   - copies the logos into Agregarr's icons folder
#   - updates templates that already exist (same name) in place, so library assignments are kept
#   - creates the ones that don't exist yet
# Runs inside an alpine container (see README); needs PACK, CONFIG and URL.
#   PACK   = folder with out/*.zip and out/icons/*.svg
#   CONFIG = Agregarr's config folder (holds settings.json and icons/)
#   URL    = Agregarr's address, e.g. http://localhost:7171
set -eu

command -v jq >/dev/null && command -v unzip >/dev/null && command -v curl >/dev/null || apk add -q curl jq unzip >/dev/null

KEY=$(jq -r '.main.apiKey // empty' "$CONFIG/settings.json")
[ -n "$KEY" ] || { echo "No API key in $CONFIG/settings.json"; exit 1; }
API="$URL/api/v1/overlay-templates"

code=$(curl -s -o /tmp/existing.json -w '%{http_code}' -H "X-API-Key: $KEY" "$API")
[ "$code" = 200 ] || { echo "Can't reach Agregarr at $URL (HTTP $code)"; exit 1; }

cp "$PACK"/out/icons/*.svg "$CONFIG/icons/"
echo "Copied $(ls "$PACK"/out/icons/*.svg | wc -l) logos into $CONFIG/icons"

created=0 updated=0 failed=0
for zip in "$PACK"/out/*.zip; do
  unzip -p "$zip" template.json \
    | jq '{name, description, type, templateData, applicationCondition}' > /tmp/body.json
  name=$(jq -r .name /tmp/body.json)
  id=$(jq -r --arg n "$name" '[.templates[] | select(.name == $n and .isDefault == false)][0].id // empty' /tmp/existing.json)

  if [ -n "$id" ]; then
    code=$(curl -s -o /tmp/resp.json -w '%{http_code}' -X PUT -H "X-API-Key: $KEY" -H 'Content-Type: application/json' --data @/tmp/body.json "$API/$id")
    action=updated
  else
    code=$(curl -s -o /tmp/resp.json -w '%{http_code}' -X POST -H "X-API-Key: $KEY" -H 'Content-Type: application/json' --data @/tmp/body.json "$API")
    action=created
  fi

  case "$code" in
    2??) echo "  $action  $name"; eval "$action=\$((\$$action + 1))" ;;
    *)   echo "  FAILED   $name (HTTP $code): $(head -c 200 /tmp/resp.json)"; failed=$((failed + 1)) ;;
  esac
done

echo "Done: $created created, $updated updated, $failed failed"
[ "$failed" = 0 ]
