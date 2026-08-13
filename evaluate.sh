#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$project_root"
dotnet run --project LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj -- "$@"
