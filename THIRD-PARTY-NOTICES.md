# Third-party notices

The source of truth for product and third-party licence disclosure is the generated manifest and complete offline texts in:

- `LyrionVoiceMcp.Web/compliance/third-party-licenses/MANIFEST.json`
- `LyrionVoiceMcp.Web/compliance/third-party-licenses/UNRESOLVED.md`
- `LyrionVoiceMcp.Web/compliance/third-party-licenses/*.txt`

The generator includes packages actually present in the production Vite bundle and packages with runtime assets in the restored API graph. Supplementary package notices are included and identical notices are deduplicated by content digest.

Run `npm run sync:third-party-licences` from `LyrionVoiceMcp.Web` after restoring the API and frontend dependencies. The command also publishes the static mirror under `LyrionVoiceMcp.Web/public/third-party-licenses`, which is distributed in the container and displayed at `/licences`.

CI runs the generator in strict mode and fails when a complete offline text cannot be resolved or committed generated files are stale.
