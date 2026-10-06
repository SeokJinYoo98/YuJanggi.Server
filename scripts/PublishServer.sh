#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet publish src/YuJanggi.Server.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  "-p:RestoreConfigFile=$PWD/NuGet.Config" \
  --output artifacts/publish/linux-x64
