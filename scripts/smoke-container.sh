#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-container.sh IMAGE [EXPECTED_ARCH]}"
expected_arch="${2:-}"
container_name="lyrion-voice-mcp-smoke-$$"

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

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

jq --exit-status '.status == "ok"' <<< "$health_json" >/dev/null
curl --fail --silent "$base_url/api/version" | jq --exit-status '.version and .channel and .build and .commit' >/dev/null
curl --fail --silent "$base_url/api/lms" | jq --exit-status '.status == "not_configured"' >/dev/null
curl --fail --silent "$base_url/api/search-observations?limit=1" \
  | jq --exit-status '.items == [] and .retentionDays == 90' >/dev/null
curl --fail --silent "$base_url/api/jobs?limit=1" \
  | jq --exit-status '(.items | type) == "array" and .retentionDays == 90' >/dev/null
curl --fail --silent "$base_url/api/scheduled-jobs" \
  | jq --exit-status 'length == 4 and any(.name == "catalogue-refresh" and .enabled == false)' >/dev/null
curl --fail --silent "$base_url/api/error-logs?limit=1" \
  | jq --exit-status '(.items | type) == "array" and .retentionDays == 90' >/dev/null
curl --fail --silent "$base_url/api/tool-calls?limit=1" \
  | jq --exit-status '(.items | type) == "array" and .retentionDays == 30' >/dev/null
curl --fail --silent "$base_url/api/evaluation" \
  | jq --exit-status '.schemaVersion == 1 and (.resolvers | index("catalogue-phuzzy-indexed")) and (.resolvers | index("catalogue-lucene")) and (.resolvers | index("catalogue-lucene-native"))' >/dev/null
docker exec "$container_name" test -r /app/licenses/Apache-2.0.txt
docker exec "$container_name" test -r /app/licenses/Lucene.Net-NOTICE.txt
docker exec "$container_name" test -r /app/licenses/Cronos-LICENSE.txt
curl --fail --silent "$base_url/" | grep --quiet 'Lyrion Voice MCP'

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
