#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
api_project="$project_root/LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj"
web_directory="$project_root/LyrionVoiceMcp.Web"

if [[ ! -d "$web_directory/node_modules" ]]; then
  echo "Run npm ci in LyrionVoiceMcp.Web first." >&2
  exit 1
fi

api_pid=""
web_pid=""

cleanup() {
  trap - INT TERM EXIT
  if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
    kill "$api_pid" 2>/dev/null || true
  fi
  if [[ -n "$web_pid" ]] && kill -0 "$web_pid" 2>/dev/null; then
    kill "$web_pid" 2>/dev/null || true
  fi
  wait "$api_pid" "$web_pid" 2>/dev/null || true
}

trap cleanup INT TERM EXIT

dotnet build "$api_project" -maxcpucount:1 -nodeReuse:false
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5600 \
LyrionVoiceMcpDevelopment__LoadLocalSettings=true \
  dotnet run --no-launch-profile --no-build --project "$api_project" &
api_pid=$!

(cd "$web_directory" && npm run dev) &
web_pid=$!

wait -n "$api_pid" "$web_pid"
