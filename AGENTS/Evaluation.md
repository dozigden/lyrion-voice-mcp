# Evaluation Guidance

Read this before changing the corpus contract, validator, benchmark runner, or generated reports.

- The canonical real evaluation corpus lives only in the permanently private sibling repository `lyrion-voice-evaluation`. Never copy real cases into this repository, committed fixtures, logs, documentation examples, or CI.
- This repository owns the corpus schema, validation, LMS baseline runner, and fictional automated test cases.
- The default local checkout shape is `lyrion-voice-mcp` and `lyrion-voice-evaluation` as sibling directories.
- Real-corpus evaluation requires the evaluation-only `LVM_EVALUATION_LMS_BASE_URL` environment variable and uses a fixed `live-evaluation` server identity. Never fall back to development settings or generic application LMS environment variables.
- A corpus case contains a stable ID, the exact query, zero or more acceptable descriptive entities, a category, and optional private notes. An empty expected array explicitly means no match.
- Expected entities use kind and title with optional artist and album constraints. Do not add LMS media IDs unless concrete ambiguity proves descriptive identity insufficient.
- Run cases sequentially so latency measurements are understandable and do not introduce artificial concurrent load.
- Generated reports belong under ignored `.data/evaluation` by default. They are evidence from a run, not part of the canonical corpus.
- Reports must not contain LMS media IDs, result references, server addresses, corpus notes, or observation IDs.
- Evaluation misses are expected benchmark outcomes and do not make the runner fail. Transport or LMS response errors do make it return a failing exit code after writing the report.
- Tests and documentation in this repository use fictional music metadata only. Tests never require the private corpus or a real LMS.
