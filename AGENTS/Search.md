# Search Guidance

Read this before changing search contracts, ranking, observation capture, catalogue ingestion, or search storage.

- The implemented first pass deliberately sends the query to LMS `search` plus playlist search to produce a usable baseline and real failure examples.
- The first-pass result order is artists, albums, tracks, then playlists, preserving LMS order within each category.
- Keep the public result contract independent of LMS response shapes and any later search engine.
- Search returns one opaque result reference per candidate; it does not return a separate public search identifier.
- Each result reference combines candidate correlation with the underlying LMS playback identity so a later `play` can record which returned candidate was selected.
- Returning the same LMS item from two searches must produce distinct result references for the two candidate occurrences.
- Artist, album, and playlist search references can seed `browse`. Preserve that candidate's correlation through derived browse descendants and continuations so eventual playback or queue addition marks the originating search result selected. Browsing alone is not a selection, and pure browse flows have no search correlation.
- The operational observation store records the original and trimmed query, resolver/version, direct LMS commands, timings, ordered candidates, zero-result searches, failures, later successful `play` selections, and human reviews.
- Record the outcome of each concurrent LMS request independently. If one request fails, retain its failure and the successful sibling request's candidates as diagnostic evidence while failing the public search call.
- Treat failed searches separately from completed searches with no results. Failed searches must not default to `no_match` or be eligible for evaluation export.
- Observation recording is best-effort: persistence failure must not fail a search or turn successful playback into a failure.
- Only mark a candidate selected after LMS playback succeeds. Do not infer retries, rephrases, or clarification from unrelated requests while the MCP contract lacks conversation context.
- Do not manufacture a confidence rating for the first-pass LMS results. Reconsider confidence only when a later resolver has meaningful ranking evidence.
- SQLite is the implemented operational observation store, not a decision about the future search index. Do not add FTS/search behaviour to it incidentally.
- Evaluation exports contain only explicitly included cases and omit observation IDs, LMS media IDs, correlation references, timestamps, and private notes.
- The observation export is review evidence, not the canonical corpus. Real curated cases live only in the permanently private `lyrion-voice-evaluation` repository; this repository contains fictional evaluation fixtures only.
- A later catalogue is canonical application data; a search index is rebuildable derived data.
- Spotify may be used only in a later offline experiment over recorded misses. It is not a runtime fallback or dependency.
- The first catalogue-backed benchmark resolver lives only in Evaluation and may read the concrete catalogue SQLite database directly. This is intentional experimental coupling, not a production search decision. It covers normalised lexical/token/prefix/bounded-edit matching only; do not describe it as phonetic or production-ready.
- Use `SEARCH_RESEARCH.md` for the existing research baseline and update it when new evidence changes the decision space.
