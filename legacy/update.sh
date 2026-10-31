#!/bin/bash
# Pull the latest version from GitHub, then apply overlays. Used by the Unraid User Scripts job.
cd "$(dirname "$0")"
docker run --rm -v "$PWD":/git alpine/git -c safe.directory=/git pull --ff-only && bash run.sh "$@"
