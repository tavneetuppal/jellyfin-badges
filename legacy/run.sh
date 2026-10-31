#!/bin/bash
# Usage: bash run.sh [--dry-run | --restore]
# Settings live in config.env (not in git):
#   JELLYFIN_URL=http://192.168.1.50:8096
#   JELLYFIN_API_KEY=xxxx
cd "$(dirname "$0")"
[ -f config.env ] || { echo "Create config.env with JELLYFIN_URL and JELLYFIN_API_KEY"; exit 1; }
docker run --rm -v "$PWD":/work -w /work --env-file config.env \
  python:3.12-slim sh -c "apt-get update -qq >/dev/null && apt-get install -y -qq libcairo2 >/dev/null 2>&1 && pip install -q --root-user-action=ignore pillow cairosvg && python badges.py $*"
