#!/bin/bash
# One-shot install/update of the Badges_ templates into Agregarr on Unraid. Safe to re-run after every change:
# existing Badges_ templates are updated in place (library assignments are kept), new ones are created.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash
#   ... | bash -s -- --libraries           preview turning the badges on in every movie library
#   ... | bash -s -- --libraries --apply   do it
#   ... | bash -s -- --handoff "Movies 4K/HDR" "TV 4K/HDR"   restore clean posters and stop Agregarr in those libraries
#                                                           (before switching them to Kometa)
#   ... | bash -s -- --revert                    stop any running sync, turn overlays off and restore original posters
#                                               in every library that still has overlays enabled
#   ... | bash -s -- --revert "Movies" "TV"      same, just for these libraries
#   ... | bash -s -- --collections               preview removing Agregarr's collections (all libraries)
#   ... | bash -s -- --collections --apply       remove them (add library names to limit it)
# Overrides: AGREGARR_CONTAINER (default agregarr), AGREGARR_CONFIG (default /mnt/user/appdata/agregarr),
#            AGREGARR_URL (default http://localhost:<port mapped by the container>)
set -euo pipefail
CONTAINER=${AGREGARR_CONTAINER:-agregarr}
CONFIG=${AGREGARR_CONFIG:-/mnt/user/appdata/agregarr}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

curl -sL https://github.com/tavneetuppal/jellyfin-badges/archive/refs/heads/main.tar.gz \
  | tar -xz -C "$TMP" jellyfin-badges-main/agregarr

if [ -z "${AGREGARR_URL:-}" ]; then
  PORT=$(docker port "$CONTAINER" 2>/dev/null | head -1 | sed 's/.*://')
  [ -n "$PORT" ] || { echo "Couldn't find Agregarr's port. Re-run with AGREGARR_URL=http://<ip>:<port>"; exit 1; }
  AGREGARR_URL="http://localhost:$PORT"
fi
echo "Agregarr: $AGREGARR_URL   config: $CONFIG"

SCRIPT=sync.sh APPLY=0 LIBS=""
while [ $# -gt 0 ]; do
  case "$1" in
    --libraries) SCRIPT=libraries.sh ;;
    --apply) APPLY=1 ;;
    --collections) SCRIPT=collections.sh; shift
                   while [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; do LIBS="${LIBS:+$LIBS|}$1"; shift; done
                   continue ;;
    --revert) SCRIPT=revert.sh; shift
              while [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; do LIBS="${LIBS:+$LIBS|}$1"; shift; done
              continue ;;
    --handoff) SCRIPT=handoff.sh; shift
               while [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; do LIBS="${LIBS:+$LIBS|}$1"; shift; done
               [ -n "$LIBS" ] || { echo "--handoff needs library names, e.g. --handoff \"Movies 4K/HDR\""; exit 1; }
               continue ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
  shift
done

docker run --rm --network host \
  -e PACK=/pack -e CONFIG=/config -e URL="$AGREGARR_URL" -e APPLY="$APPLY" -e LIBS="$LIBS" \
  -v "$TMP/jellyfin-badges-main/agregarr":/pack:ro -v "$CONFIG":/config \
  alpine sh "/pack/$SCRIPT"
