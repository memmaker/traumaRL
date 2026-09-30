#!/bin/sh
# Toolchain for the TraumaRL web port (RVIP cloud run, Ubuntu 24.04, 2026-09-30).
# .NET SDK 10 (10.0.112) from Ubuntu's archive: dot.net/dotnet-install.sh is blocked
# by the cloud egress proxy (403). Mac: bash dotnet-install.sh --channel 10.0
# --install-dir ~/.dotnet (web/build.sh adds ~/.dotnet to PATH).
# No workload needed (browser-wasm Mono interpreter; runtime pack from nuget.org).
set -e
if [ "$(uname)" = Darwin ]; then
	curl -sSLO https://dot.net/v1/dotnet-install.sh
	bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
	export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
else
	apt-get install -y dotnet-sdk-10.0
fi
dotnet --list-sdks
dotnet restore web/wasm/TraumaWeb.csproj
dotnet restore web/native/TraumaNative.csproj
# Playwright 1.56 matches Chromium 1194 under /opt/pw-browsers (cloud image).
(cd web && npm install)
