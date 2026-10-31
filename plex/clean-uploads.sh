#!/bin/sh
# Switches movies, shows and seasons whose selected poster is an UPLOADED image (e.g. an old Agregarr overlay)
# back to the metadata agent's poster. Items Kometa manages (label "Overlay") are skipped - Kometa owns those.
# Preview by default; APPLY=1 switches. LIBS="Movies|TV" limits it to those libraries (default: all movie/TV libraries).
# Needs URL and TOKEN (Plex). Runs inside alpine (see fix-posters.sh).
set -eu
command -v jq >/dev/null && command -v curl >/dev/null || apk add -q curl jq >/dev/null
get() { curl -s -H "Accept: application/json" "$URL$1$(case "$1" in *\?*) echo '&';; *) echo '?';; esac)X-Plex-Token=$TOKEN"; }
urlenc() { jq -rn --arg v "$1" '$v|@uri'; }

get /library/sections > /tmp/sections.json
jq -r '.MediaContainer.Directory[] | select(.type == "movie" or .type == "show") | "\(.key)\t\(.type)\t\(.title)"' /tmp/sections.json > /tmp/libs.tsv
[ -s /tmp/libs.tsv ] || { echo "Can't read Plex libraries at $URL (check URL/token)"; exit 1; }

found=0 fixed=0 nofallback=0
while IFS="$(printf '\t')" read -r sec kind title; do
  if [ -n "${LIBS:-}" ] && ! echo "|$LIBS|" | grep -qF "|$title|"; then continue; fi
  types=$([ "$kind" = movie ] && echo 1 || echo "2 3")   # movies | shows + seasons
  echo "== $title"
  for t in $types; do
    get "/library/sections/$sec/all?type=$t" | jq -r '.MediaContainer.Metadata[]? | "\(.ratingKey)\t\(.title)\t\(.parentTitle // "")"' > /tmp/items.tsv
    while IFS="$(printf '\t')" read -r key name parent; do
      get "/library/metadata/$key/posters" > /tmp/posters.json
      sel=$(jq -r '[.MediaContainer.Metadata[]? | select(.selected)][0].ratingKey // ""' /tmp/posters.json)
      case "$sel" in upload://*) ;; *) continue ;; esac
      if get "/library/metadata/$key" | jq -e '[.MediaContainer.Metadata[0].Label[]?.tag | ascii_downcase] | index("overlay")' >/dev/null; then
        continue   # Kometa-managed
      fi
      label="${parent:+$parent - }$name"
      alt=$(jq -r '[.MediaContainer.Metadata[]? | select(.ratingKey | startswith("upload://") | not)][0].ratingKey // ""' /tmp/posters.json)
      found=$((found + 1))
      if [ -z "$alt" ]; then echo "  $label: uploaded poster, but Plex has no other poster to switch to"; nofallback=$((nofallback + 1)); continue; fi
      if [ "${APPLY:-0}" = 1 ]; then
        code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$URL/library/metadata/$key/poster?url=$(urlenc "$alt")&X-Plex-Token=$TOKEN")
        case "$code" in 2??) echo "  fixed  $label"; fixed=$((fixed + 1)) ;; *) echo "  FAILED $label (HTTP $code)" ;; esac
      else
        echo "  would fix  $label"
      fi
    done < /tmp/items.tsv
  done
done < /tmp/libs.tsv

echo "Found $found item(s) with an uploaded poster (not managed by Kometa); fixed $fixed; $nofallback with no Plex poster to fall back to."
[ "${APPLY:-0}" = 1 ] || echo "Preview only. Re-run with --apply to switch them."
