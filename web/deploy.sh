#!/bin/sh
# Upload web/dist to https://ruzzoli.de/roguelikes/traumarl/ (only from pushed commits)
# The game needs cross-origin isolation (SharedArrayBuffer): web/coi-sw.js adds
# COOP/COEP from a service worker, so no nginx change is needed.
cd "$(dirname "$0")" && git fetch -q && [ -z "$(git status --porcelain)" ] && [ "$(git rev-parse @)" = "$(git rev-parse @{u})" ] || { echo "commit + push first"; exit 1; }
set -e
[ -f dist/index.html ] && [ -d dist/_framework ] || { echo "deploy: run web/build.sh first" >&2; exit 1; }
ssh ruzzoli.de 'sudo mkdir -p /var/www/ruzzoli.de/roguelikes/traumarl && sudo chown -R felix:www-data /var/www/ruzzoli.de/roguelikes/traumarl'
rsync -rtz --delete dist/ ruzzoli.de:/var/www/ruzzoli.de/roguelikes/traumarl/
