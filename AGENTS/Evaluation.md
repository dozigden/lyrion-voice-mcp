# Evaluation Guidance

Read this before changing the corpus contract, validator, benchmark runner, diagnostics, or generated reports.

- The canonical real evaluation corpus lives only in the permanently private sibling repository `lyrion-voice-evaluation`. Never copy real cases into this repository, fixtures, logs, documentation examples, or CI.
- This repository owns the corpus schema, validation, resolver-neutral report runner, LMS pass-through baseline, deployed production-resolver diagnostic surface, and fictional automated cases. The diagnostic runtime service belongs to Api and consumes production-neutral Search contracts; the Evaluation executable is not deployed.
- The default checkout shape is `lyrion-voice-mcp` and `lyrion-voice-evaluation` as sibling directories.
- Local real-corpus LMS baseline evaluation requires `LVM_EVALUATION_LMS_BASE_URL` and a fixed `live-evaluation` identity. Never fall back to development or application LMS settings.
- A corpus case contains a stable ID, optional exact query text, zero or more acceptable descriptive entities, category, and optional private notes. The text-match evaluator discards cases with omitted, empty, or whitespace-only query text before execution and reporting. Empty expected results on a text case explicitly mean no match.
- Expected entities use kind and title with optional artist and album constraints. Do not add LMS IDs without concrete ambiguity.
- Run cases sequentially so latency measurements are understandable.
- Generated reports belong under ignored `.data/evaluation` and must not contain LMS media IDs, result references, server addresses, corpus notes, or observation IDs.
- The local `evaluate.sh`/`evaluate.ps1` runner retains only the LMS pass-through baseline. Historical lexical, full-scan phuzzy, SQLite-lane, Lucene-lane, and native-Lucene comparators are retired; the durable selection rationale remains in `AGENTS/Search.md`.
- The deployed `/api/evaluation` surface advertises only `production`. It diagnoses the same published resolver used by MCP search and never owns a separate evaluator artifact.
- Keep Evaluation-specific corpus and report models in the Evaluation executable. Shared resolver, candidate, execution, metric, and diagnostic models belong to Search and must use production-neutral names.
- Diagnostics may expose descriptive candidates, retrieval-lane measurements, score evidence, timings, index metrics, and process memory. They must not accept or persist corpus cases, expectations, private notes, LMS IDs, references, or server configuration.
- A missing production artifact returns conflict. Diagnostic search never builds an artifact in an HTTP request, and `resolverPreparedForThisRequest` remains false for compatibility.
- Diagnostic endpoints are not MCP tools or general end-user functionality. They share the unauthenticated trusted-LAN boundary and are not safe for public exposure.
- Evaluation misses are benchmark outcomes, not runner failures. Transport and source errors are reportable failures.
- Retain qualitative private-corpus coverage for exact artist searches that collide with aligned self-titled albums. The corpus can measure descriptive retrieval and rank, while fictional Services tests remain authoritative for the exact-artist interpretation and canonical-identity decision because the evaluation case contract does not encode that application outcome.
- Tests and documentation in this repository use fictional music metadata only and never require the private corpus or a real LMS.
