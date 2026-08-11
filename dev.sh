#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project "$project_root/LyrionVoiceMcp.Dev/LyrionVoiceMcp.Dev.csproj" -- "$@"

