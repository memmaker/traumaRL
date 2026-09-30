#!/bin/sh
# Build TraumaRL for the browser into web/dist: .NET 10 SDK, browser-wasm
# (web/wasm/TraumaWeb.csproj; sources listed in web/sources.props), no workload.
# Toolchain: web/toolchain.sh. Tests: web/test.mjs, web/native (headless).
set -e
command -v dotnet >/dev/null || { export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"; }
cd "$(dirname "$0")/.."
OUT=web/dist
PUB=web/wasm/bin/Release/net10.0/publish/wwwroot
rm -rf "$OUT" "$(dirname "$PUB")"
dotnet publish web/wasm/TraumaWeb.csproj -c Release -nologo -v q | grep -v "^$" || true
[ -f "$PUB/_framework/dotnet.js" ] || { echo "publish failed"; exit 1; }
mkdir -p "$OUT"
cp -r "$PUB/_framework" "$OUT/_framework"
cp web/index.html web/trauma.js web/worker.js web/coi-sw.js "$OUT/"
cp RogueBasin/TraumaRL/bin/Debug/TraumaSprites.png RogueBasin/TraumaRL/bin/Debug/alexisv3.ttf "$OUT/"
du -sh "$OUT"
