#!/bin/bash
# Makes Kometa's config.yml cover every Plex movie/TV library with the badge overlays and the default
# tmdb + universe collections. Safe to re-run: only adds what's missing. Backs up config.yml first.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/kometa/setup-libraries.sh | bash
# SKIP_COLLECTIONS="Movies 4K/HDR|TV 4K/HDR" leaves collections out of those libraries.
set -euo pipefail
C=${KOMETA_CONFIG:-/mnt/user/appdata/kometa/config.yml}
[ -f "$C" ] || { echo "$C not found"; exit 1; }
grep -q '^libraries:' "$C" || { echo "No 'libraries:' section in $C"; exit 1; }
[ "$(grep -n '^[^ #]' "$C" | tail -1 | cut -d: -f2-)" = "libraries:" ] || { echo "'libraries:' must be the last top-level section of $C"; exit 1; }
cp "$C" "$C.bak.$(date +%Y%m%d-%H%M%S)"

# 1. Plex libraries missing from the config
if [ -z "${PLEX_LIBRARIES:-}" ]; then
  URL=$(grep -m1 ' url:' "$C" | awk '{print $2}'); TOKEN=$(grep -m1 ' token:' "$C" | awk '{print $2}')
  PLEX_LIBRARIES=$(curl -s "$URL/library/sections?X-Plex-Token=$TOKEN" | grep -o '<Directory [^>]*' \
    | grep -E 'type="(movie|show)"' | grep -o ' title="[^"]*"' | cut -d'"' -f2)
fi
[ -n "$PLEX_LIBRARIES" ] || { echo "Couldn't read Plex libraries (check url/token in $C)"; exit 1; }
tail -c1 "$C" | od -An -c | grep -q '\\n' || echo >> "$C"
while IFS= read -r L; do
  [ -n "$L" ] || continue
  if grep -qxF "  $L:" "$C"; then continue; fi
  printf '  %s:\n    overlay_files:\n      - file: config/badges/overlays.yml\n' "$L" >> "$C"
  echo "added library: $L"
done <<< "$PLEX_LIBRARIES"

# 2. tmdb + universe collections in every library block that has no collection_files yet
awk -v skip="|${SKIP_COLLECTIONS:-}|" '
  function flush() {
    if (hdr != "") {
      print hdr
      name = hdr; sub(/^  /, "", name); sub(/:[ \t]*$/, "", name)
      if (body !~ /collection_files:/ && index(skip, "|" name "|") == 0) {
        print "    collection_files:"; print "      - default: tmdb"; print "      - default: universe"
      }
      printf "%s", body
    }
    hdr = ""; body = ""
  }
  /^libraries:/ { inlib = 1; print; next }
  inlib && /^  [^ #].*:[ \t]*$/ { flush(); hdr = $0; next }
  inlib && hdr != "" { body = body $0 "\n"; next }
  { print }
  END { flush() }
' "$C" > "$C.new" && mv "$C.new" "$C"

echo "--- libraries in $C:"
sed -n '/^libraries:/,$p' "$C"
