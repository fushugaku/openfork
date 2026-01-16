#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export OPENFORK_CONFIG="${OPENFORK_CONFIG:-$ROOT_DIR/config/appsettings.json}"

dotnet run --project "$ROOT_DIR/src/OpenFork.Cli/OpenFork.Cli.csproj"
