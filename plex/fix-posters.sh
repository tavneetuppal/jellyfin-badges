#!/bin/bash
# Puts Plex's own poster back on items still showing an uploaded (old overlay) poster. Kometa-managed items are skipped.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/plex/fix-posters.sh | bash                          # preview
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/plex/fix-posters.sh | bash -s -- --apply            # do it
#   ... | bash -s -- --apply "Movies" "TV"                                                                                            # only these libraries
# Plex URL and token are read from Kometa's config (KOMETA_CONFIG, default /mnt/user/appdata/kometa/config.yml).
set -euo pipefail
C=${KOMETA_CONFIG:-/mnt/user/appdata/kometa/config.yml}
URL=$(grep -m1 ' url:' "$C" | awk '{print $2}'); TOKEN=$(grep -m1 ' token:' "$C" | awk '{print $2}')
[ -n "$URL" ] && [ -n "$TOKEN" ] || { echo "Couldn't read Plex url/token from $C"; exit 1; }
APPLY=0 LIBS=""
for a in "$@"; do case "$a" in --apply) APPLY=1 ;; *) LIBS="${LIBS:+$LIBS|}$a" ;; esac; done
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/plex/clean-uploads.sh -o "$TMP/clean-uploads.sh"
docker run --rm -e URL="$URL" -e TOKEN="$TOKEN" -e APPLY="$APPLY" -e LIBS="$LIBS" -v "$TMP":/s:ro alpine sh /s/clean-uploads.sh
