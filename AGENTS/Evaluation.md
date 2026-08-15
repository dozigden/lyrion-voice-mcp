# Evaluation Guidance

Read this before changing the corpus contract, validator, benchmark runner, or generated reports.

- The canonical real evaluation corpus lives only in the permanently private sibling repository `lyrion-voice-evaluation`. Never copy real cases into this repository, committed fixtures, logs, documentation examples, or CI.
- This repository owns the corpus schema, validation, resolver-neutral benchmark runner, LMS pass-through resolver, ring-fenced catalogue search experiments, and fictional automated test cases.
- The default local checkout shape is `lyrion-voice-mcp` and `lyrion-voice-evaluation` as sibling directories.
- Real-corpus LMS pass-through evaluation and evaluation-catalogue refresh require the evaluation-only `LVM_EVALUATION_LMS_BASE_URL` environment variable and use a fixed `live-evaluation` server identity. Never fall back to development settings or generic application LMS environment variables. Catalogue-backed resolvers do not contact LMS when reusing an existing snapshot.
- A corpus case contains a stable ID, the exact query, zero or more acceptable descriptive entities, a category, and optional private notes. An empty expected array explicitly means no match.
- Expected entities use kind and title with optional artist and album constraints. Do not add LMS media IDs unless concrete ambiguity proves descriptive identity insufficient.
- Run cases sequentially so latency measurements are understandable and do not introduce artificial concurrent load.
- `catalogue-lexical` is an evaluation-only in-memory candidate. It reads the catalogue SQLite adapter directly, records preparation time and candidate count, and currently covers artists, albums, and tracks. Its default `.data/evaluation/catalogue.db` is separate from the development and deployed catalogues. A missing snapshot is built locally from the live evaluation LMS; `--refresh-catalogue` explicitly refreshes it. Reuse one successful snapshot across comparator runs so their results remain comparable.
- Catalogue resolvers must refuse a running, failed, cancelled, interrupted, or changing latest refresh so they never benchmark partially converged batches.
- Direct catalogue SQLite access is permitted only inside the ring-fenced Evaluation project. Do not promote it into Services or the production search path; define the production catalogue-to-index boundary only after comparative evidence identifies a search approach.
- Generated reports belong under ignored `.data/evaluation` by default. They are evidence from a run, not part of the canonical corpus.
- Reports must not contain LMS media IDs, result references, server addresses, corpus notes, or observation IDs.
- Evaluation misses are expected benchmark outcomes and do not make the runner fail. Transport or LMS response errors do make it return a failing exit code after writing the report.
- Tests and documentation in this repository use fictional music metadata only. Tests never require the private corpus or a real LMS.
