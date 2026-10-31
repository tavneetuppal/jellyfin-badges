#!/bin/bash
# One-shot install/update of the Badges_ templates into Agregarr on Unraid. Safe to re-run after every change:
# existing Badges_ templates are updated in place (library assignments are kept), new ones are created.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash
#   ... | bash -s -- --libraries           preview turning the badges on in every movie library
#   ... | bash -s -- --libraries --apply   do it
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

SCRIPT=sync.sh APPLY=0
for arg in "$@"; do
  case "$arg" in
    --libraries) SCRIPT=libraries.sh ;;
    --apply) APPLY=1 ;;
    *) echo "Unknown option: $arg"; exit 1 ;;
  esac
done

docker run --rm --network host \
  -e PACK=/pack -e CONFIG=/config -e URL="$AGREGARR_URL" -e APPLY="$APPLY" \
  -v "$TMP/jellyfin-badges-main/agregarr":/pack:ro -v "$CONFIG":/config \
  alpine sh "/pack/$SCRIPT"
