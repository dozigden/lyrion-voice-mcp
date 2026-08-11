# Search Guidance

Read this before changing search contracts, ranking, observation capture, catalogue ingestion, or search storage.

- The first implementation deliberately passes queries directly to LMS to produce a usable baseline and real failure examples.
- Keep the public result contract independent of LMS response shapes and any later search engine.
- Search returns stable typed media references plus separate trace/correlation identity.
- Preserve enough evidence to associate a later `play` with the originating search when observation storage is implemented.
- Do not choose SQLite, FTS5, Lucene.NET, or another backend merely because neighbouring code uses it.
- A later catalogue is canonical application data; a search index is rebuildable derived data.
- Spotify may be used only in a later offline experiment over recorded misses. It is not a runtime fallback or dependency.
- Use `SEARCH_RESEARCH.md` for the existing research baseline and update it when new evidence changes the decision space.

