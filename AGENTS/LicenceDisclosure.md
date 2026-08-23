# Licence Disclosure Guidance

## Scope and source of truth

- Maintain one deterministic licence manifest for the production server/site deliverable.
- Include the Lyrion Voice MCP product licence, npm modules actually present in the production Vite bundle, NuGet packages with runtime assets in the restored API graph, and supplementary notices shipped by those packages.
- Do not include build-only or test-only dependencies merely because they are restored or installed.
- The canonical generated disclosure is `LyrionVoiceMcp.Web/compliance/third-party-licenses`. `MANIFEST.json`, `UNRESOLVED.md`, and every referenced text file are committed.
- The static server/site mirror is `LyrionVoiceMcp.Web/public/third-party-licenses`. The `/licences` page reads only this mirror; it must not require an API call or an external network request.

## Runtime inventories

- Vite records the npm package names present in production output in `compliance/npm-runtime-packages.json`.
- A normal production build fails when that inventory is stale. Run `npm run refresh:licence-inventory`, review the changed inventory, then regenerate the disclosure.
- NuGet inclusion comes from runtime, native, resource, and runtime-target asset groups in `LyrionVoiceMcp.Api/obj/project.assets.json` for `net10.0`.
- Run `dotnet restore LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj --locked-mode -maxcpucount:1 -nodeReuse:false` before regenerating when backend dependencies changed.

## Generation and review

- Run `npm run sync:third-party-licences` from `LyrionVoiceMcp.Web` after dependency changes. Use `-- --strict` when validating locally.
- Strict generation must resolve a complete offline text for every included package. A licence URL, SPDX expression, or package metadata stub alone is not sufficient.
- Package-provided texts are preferred. Expression-based NuGet packages may use the reviewed MIT text derived from the product licence or the canonical Apache 2.0 text under `compliance/licence-texts`.
- When NuGet metadata names a licence file instead of an SPDX identifier, keep any reviewed package/version-specific identifier in the generator exact and reassess it on upgrades. Preserve the metadata filename separately from the displayed identifier.
- Put a reviewed fallback at the path named in `UNRESOLVED.md` only when package metadata is incomplete. Keep the fallback package/version-specific and reassess it on upgrades.
- Supplementary NuGet notices are separate manifest entries and are deduplicated by normalised-text SHA-256.
- Notice entries are not licence identifiers. Label them as notices and retain the exact covered package names and versions in the manifest and UI.
- Review dependency names, versions, declared licences, source paths, unresolved entries, and material notice changes before committing generated output.

## Distribution and CI

- The Vite public mirror is copied into `/app/wwwroot/third-party-licenses` by the normal frontend build; do not maintain a second hand-curated container licence directory.
- Container smoke tests verify the manifest, all referenced texts, and the `/licences` route.
- CI restores both dependency graphs, runs strict synchronisation, and fails if either generated tree is dirty.
