#!/usr/bin/env bash
set -euo pipefail

digest_directory="${1:?usage: promote-container-digests.sh DIGEST_DIRECTORY METADATA_JSON GHCR_IMAGE DOCKERHUB_IMAGE [--dry-run]}"
metadata_json="${2:?metadata JSON is required}"
ghcr_image="${3:?GHCR image is required}"
dockerhub_image="${4:?Docker Hub image is required}"
mode="${5:-}"

if [[ "$mode" != "" && "$mode" != "--dry-run" ]]; then
  echo "Unknown option: $mode" >&2
  exit 1
fi

shopt -s nullglob
digest_files=("$digest_directory"/*)
if [[ "${#digest_files[@]}" -ne 2 ]]; then
  echo "Expected two platform digests, found ${#digest_files[@]}." >&2
  exit 1
fi

digests=()
for digest_file in "${digest_files[@]}"; do
  digest_name="$(basename "$digest_file")"
  if ! [[ "$digest_name" =~ ^[0-9a-f]{64}$ ]]; then
    echo "Invalid platform digest artifact: $digest_name" >&2
    exit 1
  fi
  digests+=("sha256:$digest_name")
done

results_directory="$(mktemp -d)"
trap 'rm -rf "$results_directory"' EXIT

write_tags() {
  local image="${1:?image is required}"
  local output_file="${2:?output file is required}"

  jq -r --arg image "$image" \
    '.tags[] | select(startswith($image + ":"))' \
    <<< "$metadata_json" > "$output_file"

  if [[ ! -s "$output_file" ]]; then
    echo "No generated tags found for $image." >&2
    exit 1
  fi
}

write_staged_platforms() {
  local image="${1:?image is required}"
  local output_file="${2:?output file is required}"

  : > "$output_file"
  for digest in "${digests[@]}"; do
    docker buildx imagetools inspect --raw "$image@$digest" \
      | jq -r '
        .manifests[]
        | select(.platform.os == "linux")
        | select(.platform.architecture == "amd64" or .platform.architecture == "arm64")
        | "\(.platform.architecture)=\(.digest)"
      ' >> "$output_file"
  done
  sort --output "$output_file" "$output_file"

  if [[ "$(cut -d= -f1 "$output_file")" != $'amd64\narm64' ]]; then
    echo "Staged sources for $image do not contain exactly linux/amd64 and linux/arm64." >&2
    exit 1
  fi
}

write_manifest_platforms() {
  local reference="${1:?manifest reference is required}"
  local output_file="${2:?output file is required}"
  local raw_manifest

  raw_manifest="$(docker buildx imagetools inspect --raw "$reference")"
  if ! jq --exit-status '
    [.manifests[]
      | select(.platform.os == "linux")
      | .platform.architecture]
    | sort == ["amd64", "arm64"]
  ' <<< "$raw_manifest" >/dev/null; then
    echo "Manifest $reference does not contain exactly linux/amd64 and linux/arm64." >&2
    exit 1
  fi

  jq -r '
    [.manifests[]
      | select(.platform.os == "linux")
      | select(.platform.architecture == "amd64" or .platform.architecture == "arm64")
      | "\(.platform.architecture)=\(.digest)"]
    | sort
    | .[]
  ' <<< "$raw_manifest" > "$output_file"
}

retry_create() {
  local attempt

  for attempt in 1 2 3; do
    if docker buildx imagetools create "$@"; then
      return
    fi

    if [[ "$attempt" -lt 3 ]]; then
      echo "Manifest write failed on attempt $attempt; retrying." >&2
      sleep "$attempt"
    fi
  done

  echo "Manifest write failed after three attempts." >&2
  return 1
}

create_candidate() {
  local image="${1:?image is required}"
  local expected_file="${2:?expected platforms file is required}"
  local result_file="${3:?result file is required}"
  local candidate_reference="$image:promotion-candidate"
  local sources=()

  for digest in "${digests[@]}"; do
    sources+=("$image@$digest")
  done

  retry_create --tag "$candidate_reference" "${sources[@]}"
  write_manifest_platforms "$candidate_reference" "$result_file"

  if ! cmp --silent "$expected_file" "$result_file"; then
    echo "Candidate manifest for $image does not contain the validated platform digests." >&2
    exit 1
  fi
}

publish_verified_candidate() {
  local image="${1:?image is required}"
  local tags_file="${2:?tags file is required}"
  local expected_file="${3:?expected platforms file is required}"
  local candidate_reference="$image:promotion-candidate"
  local actual_file
  local tag
  local tag_args=()

  while IFS= read -r tag; do
    tag_args+=(--tag "$tag")
  done < "$tags_file"

  retry_create "${tag_args[@]}" "$candidate_reference"

  while IFS= read -r tag; do
    actual_file="$results_directory/final-$(printf '%s' "$tag" | sha256sum | cut -d' ' -f1)"
    write_manifest_platforms "$tag" "$actual_file"
    if ! cmp --silent "$expected_file" "$actual_file"; then
      echo "Published manifest $tag does not contain the validated platform digests." >&2
      exit 1
    fi
  done < "$tags_file"
}

ghcr_tags="$results_directory/ghcr-tags"
dockerhub_tags="$results_directory/dockerhub-tags"
write_tags "$ghcr_image" "$ghcr_tags"
write_tags "$dockerhub_image" "$dockerhub_tags"

if [[ "$mode" == "--dry-run" ]]; then
  echo "Container manifest promotion inputs are valid."
  exit 0
fi

ghcr_expected="$results_directory/ghcr-expected"
dockerhub_expected="$results_directory/dockerhub-expected"
write_staged_platforms "$ghcr_image" "$ghcr_expected"
write_staged_platforms "$dockerhub_image" "$dockerhub_expected"

if ! cmp --silent "$ghcr_expected" "$dockerhub_expected"; then
  echo "GHCR and Docker Hub staged platform digests differ." >&2
  exit 1
fi

ghcr_candidate="$results_directory/ghcr-candidate"
dockerhub_candidate="$results_directory/dockerhub-candidate"
create_candidate "$ghcr_image" "$ghcr_expected" "$ghcr_candidate"
create_candidate "$dockerhub_image" "$dockerhub_expected" "$dockerhub_candidate"

if ! cmp --silent "$ghcr_candidate" "$dockerhub_candidate"; then
  echo "GHCR and Docker Hub candidate manifests differ." >&2
  exit 1
fi

publish_verified_candidate "$ghcr_image" "$ghcr_tags" "$ghcr_candidate"
publish_verified_candidate "$dockerhub_image" "$dockerhub_tags" "$dockerhub_candidate"

echo "Published equivalent verified amd64 and arm64 manifests to GHCR and Docker Hub."
