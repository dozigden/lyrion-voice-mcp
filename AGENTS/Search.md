# Search Guidance

Read this before changing search contracts, ranking, observation capture, catalogue ingestion, or search storage.

- The first implementation deliberately passes queries directly to LMS to produce a usable baseline and real failure examples.
- Keep the public result contract independent of LMS response shapes and any later search engine.
- Search returns one opaque result reference per candidate; it does not return a separate public search identifier.
- Each result reference combines candidate correlation with the underlying LMS playback identity so a later `play` can record which returned candidate was selected.
- Returning the same LMS item from two searches must produce distinct result references for the two candidate occurrences.
- Preserve enough evidence to associate a later `play` with the originating query, candidate position, and available match evidence when observation storage is implemented.
- Do not manufacture a confidence rating for the first-pass LMS results. Reconsider confidence only when a later resolver has meaningful ranking evidence.
- Do not choose SQLite, FTS5, Lucene.NET, or another backend merely because neighbouring code uses it.
- A later catalogue is canonical application data; a search index is rebuildable derived data.
- Spotify may be used only in a later offline experiment over recorded misses. It is not a runtime fallback or dependency.
- Use `SEARCH_RESEARCH.md` for the existing research baseline and update it when new evidence changes the decision space.
