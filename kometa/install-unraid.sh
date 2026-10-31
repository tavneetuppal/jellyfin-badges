#!/bin/bash
# Installs/updates the badge overlay file + images into Kometa's config folder on Unraid. Safe to re-run.
#   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/kometa/install-unraid.sh | bash
# Override the folder with KOMETA_CONFIG (default /mnt/user/appdata/kometa).
set -euo pipefail
CONFIG=${KOMETA_CONFIG:-/mnt/user/appdata/kometa}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
[ -d "$CONFIG" ] || { echo "$CONFIG doesn't exist. Install Kometa first (or set KOMETA_CONFIG)."; exit 1; }

curl -sL https://github.com/tavneetuppal/jellyfin-badges/archive/refs/heads/main.tar.gz \
  | tar -xz -C "$TMP" jellyfin-badges-main/kometa/out
mkdir -p "$CONFIG/badges"
rm -rf "$CONFIG/badges/images"
cp -r "$TMP/jellyfin-badges-main/kometa/out/." "$CONFIG/badges/"
echo "Installed $(ls "$CONFIG/badges/images" | wc -l) images and overlays.yml into $CONFIG/badges"
echo "In config.yml, add to each library:   overlay_files: [ {file: config/badges/overlays.yml} ]"
