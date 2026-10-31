#!/bin/bash
# Makes Kometa's config.yml cover every Plex movie/TV library with the badge overlays and the default
# tmdb + universe collections. Safe to re-run: only adds what's missing. Backs up config.yml first.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/kometa/setup-libraries.sh | bash
# Libraries with "4K" in the name get no collections (avoids duplicating the main libraries' collections);
# SKIP_COLLECTIONS="Kids|Anime" leaves collections out of more libraries.
# Libraries listed (one per line) in badges-exclude-libraries.txt next to config.yml are never added
# (e.g. home videos).
set -euo pipefail
C=${KOMETA_CONFIG:-/mnt/user/appdata/kometa/config.yml}
[ -f "$C" ] || { echo "$C not found"; exit 1; }
grep -q '^libraries:' "$C" || { echo "No 'libraries:' section in $C"; exit 1; }
cp "$C" "$C.bak.$(date +%Y%m%d-%H%M%S)"

# 1. Plex libraries missing from the config
if [ -z "${PLEX_LIBRARIES:-}" ]; then
  URL=$(grep -m1 ' url:' "$C" | awk '{print $2}'); TOKEN=$(grep -m1 ' token:' "$C" | awk '{print $2}')
  PLEX_LIBRARIES=$(curl -s "$URL/library/sections?X-Plex-Token=$TOKEN" | grep -o '<Directory [^>]*' \
    | grep -E 'type="(movie|show)"' | grep -o ' title="[^"]*"' | cut -d'"' -f2)
fi
[ -n "$PLEX_LIBRARIES" ] || { echo "Couldn't read Plex libraries (check url/token in $C)"; exit 1; }
EXCLUDE="$(dirname "$C")/badges-exclude-libraries.txt"
ADD=$(mktemp)
while IFS= read -r L; do
  [ -n "$L" ] || continue
  if [ -f "$EXCLUDE" ] && grep -qxF "$L" "$EXCLUDE"; then continue; fi
  if grep -qxF "  $L:" "$C" || grep -qxF "  \"$L\":" "$C" || grep -qxF "  '$L':" "$C"; then continue; fi
  printf '  %s:\n    overlay_files:\n      - file: config/badges/overlays.yml\n' "$L" >> "$ADD"
  echo "added library: $L"
done <<< "$PLEX_LIBRARIES"
# insert at the end of the libraries: section (Kometa appends other sections after it when it rewrites the file)
awk -v addfile="$ADD" '
  function emit() { if (!done) { while ((getline line < addfile) > 0) print line; done = 1 } }
  /^libraries:/ { inlib = 1; print; next }
  inlib && /^[^ #]/ { emit(); inlib = 0 }
  { print }
  END { if (inlib) emit() }
' "$C" > "$C.new" && mv "$C.new" "$C"
rm -f "$ADD"

# 2. tmdb + universe collections in every library block that has no collection_files yet.
#    Libraries with "4K" in the name (and any in SKIP_COLLECTIONS) get none: the default tmdb/universe entries are
#    removed from them if an earlier run added them; other collections there are left alone.
awk -v skip="|${SKIP_COLLECTIONS:-}|" '
  function skipped(name) { return index(skip, "|" name "|") > 0 || toupper(name) ~ /4K/ }
  function flush(   i, k, has, name, out, n) {
    if (hdr == "") return
    print hdr
    name = hdr; sub(/^  /, "", name); sub(/:[ \t]*$/, "", name); gsub(/^["\x27]|["\x27]$/, "", name)
    has = 0
    for (i = 1; i <= nb; i++) if (body[i] ~ /^    collection_files:/) has = 1
    if (skipped(name)) {
      # drop our default entries, then a collection_files key left with no entries
      n = 0
      for (i = 1; i <= nb; i++) if (body[i] !~ /^[ \t]*- default: (tmdb|universe)[ \t]*$/) out[++n] = body[i]
      for (i = 1; i <= n; i++) {
        if (out[i] ~ /^    collection_files:[ \t]*$/ && (i == n || out[i + 1] !~ /^(      |    - )/)) continue
        print out[i]
      }
    } else {
      if (!has) { print "    collection_files:"; print "      - default: tmdb"; print "      - default: universe" }
      for (i = 1; i <= nb; i++) print body[i]
    }
    hdr = ""; nb = 0
  }
  /^libraries:/ { inlib = 1; print; next }
  inlib && /^[^ #]/ { flush(); inlib = 0 }
  inlib && /^  [^ #].*:[ \t]*$/ { flush(); hdr = $0; next }
  inlib && hdr != "" { body[++nb] = $0; next }
  { print }
  END { flush() }
' "$C" > "$C.new" && mv "$C.new" "$C"

echo "--- libraries in $C:"
awk '/^libraries:/{p=1;print;next} p&&/^[^ #]/{p=0} p' "$C"
