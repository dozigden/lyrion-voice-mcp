#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-container.sh IMAGE [EXPECTED_ARCH]}"
expected_arch="${2:-}"
container_name="lyrion-voice-mcp-smoke-$$"

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

assert_json() {
  local description="${1:?description is required}"
  local expression="${2:?jq expression is required}"
  local response="${3-}"

  if ! jq --exit-status "$expression" <<< "$response" >/dev/null; then
    echo "$description returned unexpected JSON:" >&2
    echo "$response" >&2
    exit 1
  fi
}

check_json_endpoint() {
  local path="${1:?endpoint path is required}"
  local description="${2:?description is required}"
  local expression="${3:?jq expression is required}"
  local response

  if ! response="$(curl --fail --silent --show-error "$base_url$path")"; then
    echo "Could not read $description from $path." >&2
    exit 1
  fi

  assert_json "$description" "$expression" "$response"
}

if [[ -n "$expected_arch" ]]; then
  actual_arch="$(docker image inspect "$image" --format '{{.Architecture}}')"
  if [[ "$actual_arch" != "$expected_arch" ]]; then
    echo "Expected image architecture $expected_arch, got $actual_arch." >&2
    exit 1
  fi
fi

docker run --detach --name "$container_name" --publish 127.0.0.1::5600 "$image" >/dev/null
host_port="$(docker port "$container_name" 5600/tcp | sed -n 's/.*://p')"
base_url="http://127.0.0.1:$host_port"

health_json=""
for _ in {1..30}; do
  if health_json="$(curl --fail --silent "$base_url/api/health" 2>/dev/null)"; then
    break
  fi

  if [[ "$(docker inspect --format '{{.State.Running}}' "$container_name")" != "true" ]]; then
    docker logs "$container_name"
    exit 1
  fi

  sleep 2
done

if [[ -z "$health_json" ]]; then
  echo "Container did not become healthy within 60 seconds." >&2
  docker logs "$container_name"
  exit 1
fi

assert_json "Health endpoint" '.status == "ok"' "$health_json"
version_json="$(curl --fail --silent --show-error "$base_url/api/version")"
if [[ -n "${EXPECTED_VERSION:-}${EXPECTED_CHANNEL:-}${EXPECTED_BUILD:-}${EXPECTED_COMMIT:-}" ]]; then
  if [[ -z "${EXPECTED_VERSION:-}" || -z "${EXPECTED_CHANNEL:-}" \
    || -z "${EXPECTED_BUILD:-}" || -z "${EXPECTED_COMMIT:-}" ]]; then
    echo "Expected version, channel, build and commit must all be supplied together." >&2
    exit 1
  fi

  if ! jq --exit-status \
    --arg version "$EXPECTED_VERSION" \
    --arg channel "$EXPECTED_CHANNEL" \
    --arg build "$EXPECTED_BUILD" \
    --arg commit "$EXPECTED_COMMIT" \
    '.version == $version and .channel == $channel and .build == $build and .commit == $commit' \
    <<< "$version_json" >/dev/null; then
    echo "Version endpoint returned unexpected build metadata:" >&2
    echo "$version_json" >&2
    exit 1
  fi
else
  assert_json "Version endpoint" '.version and .channel and .build and .commit' "$version_json"
fi
check_json_endpoint "/api/lms" "LMS endpoint" '.status == "not_configured"'
check_json_endpoint "/api/search-observations?limit=1" "Search observation endpoint" \
  '.items == [] and .retentionDays == 90'
check_json_endpoint "/api/jobs?limit=1" "Jobs endpoint" \
  '(.items | type) == "array" and .retentionDays == 90'
check_json_endpoint "/api/scheduled-jobs" "Scheduled jobs endpoint" \
  'length == 4 and any(.name == "catalogue-refresh" and .enabled == false)'
check_json_endpoint "/api/error-logs?limit=1" "Error logs endpoint" \
  '(.items | type) == "array" and .retentionDays == 90'
check_json_endpoint "/api/tool-calls?limit=1" "Tool calls endpoint" \
  '(.items | type) == "array" and .retentionDays == 30'
check_json_endpoint "/api/evaluation" "Evaluation endpoint" \
  '(.schemaVersion | type) == "number" and .schemaVersion >= 1 and .resolvers == ["production"]'
check_json_endpoint "/api/search/index" "Search index endpoint" \
  '.resolver == "catalogue-phuzzy-sqlite" and .artifact == null'
licence_manifest="$(curl --fail --silent "$base_url/third-party-licenses/manifest.json")"
assert_json "Licence manifest" \
  '.unresolvedPackages == [] and any(.copiedLicences[]; .ecosystem == "product") and any(.copiedLicences[]; .ecosystem == "npm") and any(.copiedLicences[]; .ecosystem == "nuget")' \
  "$licence_manifest"
while IFS= read -r licence_output; do
  curl --fail --silent --output /dev/null \
    "$base_url/third-party-licenses/$(basename "$licence_output")"
done < <(jq --raw-output '.copiedLicences[].outputFile' <<< "$licence_manifest")
docker exec "$container_name" test -r /data/lyrion-voice-mcp.db
curl --fail --silent "$base_url/" | grep --quiet 'Lyrion Voice MCP'
curl --fail --silent "$base_url/licences" | grep --quiet 'Lyrion Voice MCP'

mcp_status="$(curl --silent --output /tmp/lyrion-voice-mcp-smoke-mcp-$$.json --write-out '%{http_code}' \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --header 'MCP-Protocol-Version: 2026-07-28' \
  --header 'Mcp-Method: server/discover' \
  --data '{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"container-smoke","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}' \
  "$base_url/mcp")"
if [[ "$mcp_status" != "200" ]]; then
  echo "MCP discovery returned HTTP $mcp_status." >&2
  cat "/tmp/lyrion-voice-mcp-smoke-mcp-$$.json" >&2
  exit 1
fi
rm -f "/tmp/lyrion-voice-mcp-smoke-mcp-$$.json"

tools_response="$(curl --fail --silent \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --header 'MCP-Protocol-Version: 2026-07-28' \
  --header 'Mcp-Method: tools/list' \
  --data '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"container-smoke","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}' \
  "$base_url/mcp")"
tools_json="$(sed -n 's/^data: //p' <<< "$tools_response")"
if ! jq --exit-status \
  '(["control_player", "get_player_status", "get_queue", "manage_queue", "play", "search"] - [.result.tools[].name]) == []' \
  <<< "$tools_json" >/dev/null; then
  echo "MCP tools/list did not return all required implemented tools." >&2
  echo "$tools_response" >&2
  exit 1
fi

echo "Container smoke test passed for $image."
