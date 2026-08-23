# Voice-Tolerant Media Search Research

Research date: 2026-08-11

Catalogue ingestion evidence and the provisional canonical model are recorded separately in [CATALOGUE_RECONNAISSANCE.md](CATALOGUE_RECONNAISSANCE.md).

## Production decision (2026-08-16)

The bounded SQLite lane resolver was selected as the first production engine because it retained the observed corpus quality while materially outperforming both Lucene variants on query latency. It now lives in `LyrionVoiceMcp.Search` as `catalogue-phuzzy-sqlite` version 1, consumes storage-neutral 500-document catalogue batches, and publishes validated generation artifacts through durable jobs. Numeric tokens are expanded to spoken forms before Double Metaphone encoding so a digit cannot disappear while claiming complete-span phonetic evidence.

The lexical, full-scan phuzzy, lane-Lucene, and native-Lucene implementations and deployed artifacts were retired. Their measurements below remain historical evidence, not runnable options. Playlist discovery stays as an isolated LMS lane; confidence, calibrated no-match behaviour, corrections, ratings, and virtual-library filters remain later work.

## Historical benchmark implementation

The offline evaluator now has a resolver-neutral execution path and a first `catalogue-lexical` candidate. It builds a separate local evaluation catalogue from the live evaluation LMS when the snapshot is missing or `--refresh-catalogue` is supplied, allowing every comparator to reuse identical canonical input. The experiment reads that successfully converged SQLite database directly, loads artist, album, and track candidates into memory, and applies Unicode/diacritic/punctuation normalisation, exact and reordered tokens, prefixes, cross-field token coverage, and bounded Levenshtein distance. It records candidate count, preparation duration, result quality, and per-query latency through the existing privacy-safe report.

The second `catalogue-phuzzy` candidate reuses the same catalogue loader and adds transliteration, partial query spans, joined forms, character trigrams, a deliberately simple consonant skeleton, uppercase-name spoken aliases, and query-coverage ranking. Private evaluation showed that these signals can retrieve and rank speech-damaged names that the lexical comparator misses, while also exposing the scaling cost of scanning every candidate for every query span.

The third `catalogue-phuzzy-indexed` candidate puts bounded retrieval in front of the same scorer. A disposable SQLite database supplies normalised, compact, acronym, consonant-skeleton, Double Metaphone, token/prefix, and trigram lanes. Private evaluation retained the relevant quality improvement while materially reducing query cost. This is evidence that lane retrieval removes full-scan latency, not that SQLite must be the final backend.

The fourth `catalogue-lucene` candidate uses bounded exact, prefix, token, fuzzy, trigram, consonant-skeleton, acronym, and Double Metaphone retrieval before the shared scorer. It retained the relevant search quality and produced a smaller derived index with richer fuzzy primitives, but its query path was slower than the SQLite lanes in the observed environment.

The fifth `catalogue-lucene-native` candidate tests capabilities hidden by that shared-scoring comparison. It indexes field-specific exact, compact, tokenised, acronym, consonant-skeleton, and Double Metaphone forms, then executes one coverage-weighted disjunction-max query containing exact spans, phrase slop, token, prefix, fuzzy, acronym, and phonetic clauses. The best clause dominates while a small tie-break contribution rewards corroborating signals, preventing several correlated weak matches from simply summing past a stronger match. Lucene supplies final ordering rather than returning a union to the phuzzy scorer. Its diagnostic score is scaled only to fit the existing evidence contract and is neither confidence nor numerically comparable with phuzzy scores. Target-device and repeated performance measurements remain required before drawing a broader backend conclusion.

The Double Metaphone integration indexes and queries both primary and alternate codes. A fictional `Nite` to `Knight` case proves the phonetic lane can retrieve and score a result that the acronym, consonant-skeleton, trigram and bounded-edit signals miss. Evaluation also exposed a false-positive mode when individual-token codes were pooled into a multiword span; encoding complete spans preserved the scorer's ignored-token penalty. This is why phonetic matches remain one weighted signal rather than confidence by themselves.

The Lucene dependency set was audited before addition: `Lucene.Net`, `Lucene.Net.Analysis.Common`, and `Lucene.Net.Analysis.Phonetic` 4.8.0-beta00018 are Apache-2.0 packages containing their full combined licence and NOTICE files. Their resolved transitives are Apache-2.0 `J2N` 2.1.0 and MIT-licensed Microsoft configuration abstractions/primitives 8.0.0. The production resolver retains `Lucene.Net.Analysis.Phonetic` solely for Double Metaphone; the Lucene index comparators themselves are retired.

These were deliberately ring-fenced experiments. Their private results were enough to compare architectures, not to establish general quality or false-positive behaviour. Continued private-corpus evaluation of the selected production artifact remains necessary.

## Conclusion

The application database and media search engine remain separate decisions even though both currently use SQLite.

The product should have three explicit boundaries:

1. A canonical media catalogue populated from LMS.
2. A rebuildable search index derived from that catalogue.
3. An application-owned resolver that combines and reranks candidate signals and records diagnostics.

SQLite is used independently for canonical metadata and the first production search artifact. Neither concrete database is exposed as the resolver or catalogue contract, so later evidence can replace the search backend without reshaping canonical storage. Confidence and no-match calibration have not been inferred from ranking scores.

## Why one search technique is insufficient

Voice-transcription errors and music metadata produce several distinct retrieval problems:

- Exact names with differences in case, punctuation, articles, diacritics, Unicode forms, featured-artist notation, or word order.
- Ordinary spelling errors that are close under edit distance.
- Transcriptions that sound similar but are textually different.
- Acronyms and stylised names whose written form does not express the pronunciation.
- Alternative names, transliterations, former names, and common misspellings.
- Partial requests containing a title plus an artist, album, year, or entity-type hint.
- Genuine ambiguity between similarly named artists, albums, and tracks.

The fictional artist `ZYRAQ` transcribed as `zyrack` demonstrates the limits particularly well. It is not a small typo, has weak character-trigram overlap, and may not share a classic phonetic code because the written artist name is stylised. A known spoken alias or a genuine pronunciation representation is likely to be more useful than raising an edit-distance threshold until unrelated results match.

## Recommended retrieval pipeline

### 1. Preserve and normalise

Keep the original metadata, then produce separate search forms. Candidate normalisation includes Unicode normalization and case folding, whitespace and punctuation handling, diacritic-insensitive forms, conservative article handling, and normalized featured-artist syntax.

Do not destructively replace the display metadata. Different normalization forms should be independent indexed fields or terms so their contributions remain observable.

### 2. Add aliases and pronunciations

Index aliases as first-class terms linked to the same stable media entity. Sources may include:

- LMS metadata and sort names.
- User-reviewed corrections from the search-history UI.
- Explicit local pronunciation aliases.
- Optional external metadata such as MusicBrainz aliases and search hints.
- Later, grapheme-to-phoneme output or curated phoneme sequences.

MusicBrainz explicitly models localized aliases, alternative artist names, and search hints for misspellings and variants: <https://musicbrainz.org/doc/Aliases>.

### 3. Generate candidates through multiple lanes

Each lane should return stable entity ids and lane-specific evidence, not final confidence:

- Exact, prefix, token, phrase, and field-weighted full-text matches.
- Character n-gram/trigram similarity for substrings and several-character corruption.
- Damerau-Levenshtein or Levenshtein matching for insertions, deletions, substitutions, and transpositions.
- A fast delete-based spelling candidate structure such as SymSpell for comparison.
- Phonetic keys such as Double Metaphone, Beider-Morse, Caverphone, Cologne, or Daitch-Mokotoff, selected with language limitations understood.
- Explicit aliases and learned transcription-to-entity corrections.
- Later, true phoneme-sequence matching using grapheme-to-phoneme conversion.
- Later, semantic/vector retrieval for descriptive requests; general text embeddings should not be assumed to solve pronunciation errors.

### 4. Rerank in the application

The resolver should own final ranking so it can combine:

- Per-lane scores and exact-match indicators.
- Which field matched: artist, title, album, alias, or pronunciation.
- Entity-kind constraints inferred or supplied by the caller.
- Artist/album/track relationships and agreement between request components.
- Score margins and candidate ambiguity.
- Optional collection, provider, locale, and user context.
- Later, reviewed outcomes and carefully bounded popularity/history signals.

Raw scores from FTS5, Lucene, PostgreSQL, or another engine are not probabilities and must not be returned as confidence without calibration.

### 5. Calibrate confidence from reviewed searches

Return a preferred candidate, alternatives, score margin, match evidence, and an application-calibrated confidence value or band. Calibrate against the labelled search corpus collected through the Vue review UI. Track false confident matches explicitly; they matter more in a playback assistant than harmless low-ranked misses.

## Backend options

| Option | Useful capabilities | Advantages | Constraints and risks |
|---|---|---|---|
| Custom in-memory derived index | Dictionaries for exact/alias/phonetic forms, precomputed n-grams, SymSpell-style deletes, application scoring | Embedded, very low call overhead, completely transparent ranking, easy immutable snapshot swap | We own indexing and candidate-generation correctness; memory and larger-library behaviour require benchmarks |
| SQLite plus FTS5 | Token/prefix full-text search, BM25, built-in trigram tokenizer, custom tokenizer API | Embedded, simple deployment, can live beside canonical data | FTS5 does not provide general edit-distance fuzzy search; trigram queries shorter than three characters have limitations; custom tokenizers are native APIs |
| SQLite `spellfix1` | Vocabulary correction, edit distance, configurable costs, phonetic hash, explicit `soundslike` values | Interesting experimental candidate for names and learned pronunciations | Not included in the SQLite amalgamation or standard builds; requires compiling and shipping a native extension across platforms |
| Lucene.NET | Fielded BM25 search, fuzzy queries using Damerau-Levenshtein, n-gram analyzers, phonetic analysis package, boosts and explanations | Rich embedded search engine and a natural match for multi-lane fields | Current 4.8 release remains beta/pre-release and is a port of an older Java Lucene generation; index upgrades and package risk need evaluation |
| PostgreSQL | Indexed trigram similarity through `pg_trgm`; Soundex, Daitch-Mokotoff, Levenshtein, Metaphone, and Double Metaphone through `fuzzystrmatch`; optional vectors through pgvector | Strong database-native experimentation and good operational visibility | Requires a separate database service; several classic phonetic functions have documented multibyte limitations; still requires application reranking |
| Meilisearch | Built-in prefix/Damerau-Levenshtein typo tolerance and ranking | Simple turnkey typo-tolerant baseline | Defaults allow no typo for words shorter than five characters and cap matches at two typos; not pronunciation-aware; separate service |
| Typesense | Typo tolerance, field weighting, infix search, filtering, optional voice and hybrid vector search | Turnkey and feature rich | Separate service; built-in voice search would couple transcription and retrieval differently from the MCP design; phonetic aliases still need modelling |
| Elasticsearch/OpenSearch | Fielded full-text search, fuzzy queries, n-grams, phonetic analyzers/plugins, rescoring, vectors | Broadest mature search feature set | Disproportionately heavy deployment for a typical home library; operational and packaging burden |

### SQLite assessment

SQLite FTS5 supports BM25 ranking, prefix indexes, a trigram tokenizer, and custom tokenizers. Its trigram tokenizer is useful for substring matching, but strings shorter than three Unicode characters do not match normal FTS trigram queries. SQLite's `spellfix1` is more directly relevant to fuzzy vocabulary matching and supports a `soundslike` value, but it is explicitly not part of standard SQLite builds. Shipping it would introduce native-extension packaging on every supported platform.

Therefore SQLite can participate in a search implementation, but plain FTS5 should not be assumed to solve voice-tolerant media resolution by itself.

Official references:

- <https://www.sqlite.org/fts5.html>
- <https://www.sqlite.org/spellfix1.html>
- <https://www.sqlite.org/loadext.html>

### Lucene.NET assessment

Lucene.NET is the most capable embedded search candidate. Its fuzzy query uses Damerau-Levenshtein, and its phonetic analysis package includes multiple phonetic filters. It can keep exact, normalized, n-gram, alias, and phonetic representations in separate boosted fields, which is the right shape for explainable multi-lane retrieval.

The trade-off is dependency maturity. The latest 4.8 package is still labelled beta, although the Apache project describes it as heavily tested and used in production. It should be benchmarked and subjected to index rebuild/upgrade tests rather than adopted without a spike.

Official references:

- <https://lucenenet.apache.org/>
- <https://lucenenet.apache.org/download/version-4.8.0-beta00018.html>
- <https://lucenenet.apache.org/docs/4.8.0-beta00016/api/core/Lucene.Net.Search.FuzzyQuery.html>
- <https://www.nuget.org/packages/Lucene.Net.Analysis.Phonetic/>

### PostgreSQL assessment

PostgreSQL is the strongest relational alternative if a separate service is acceptable. `pg_trgm` provides index-supported string and word similarity. `fuzzystrmatch` includes edit-distance and multiple phonetic functions. Npgsql exposes these extensions to EF Core. PostgreSQL also has a mature vector-search path if semantic retrieval later becomes useful.

It should be retained as a viable deployment/search adapter, especially for larger libraries or multi-instance installations, without making it a requirement for a simple home deployment.

Official references:

- <https://www.postgresql.org/docs/current/pgtrgm.html>
- <https://www.postgresql.org/docs/current/fuzzystrmatch.html>
- <https://github.com/pgvector/pgvector>

### Dedicated search services

Meilisearch and Typesense provide valuable low-effort typo-tolerant baselines. Elasticsearch or OpenSearch can implement almost every lexical and phonetic lane, but their operational cost is difficult to justify initially. None removes the need for aliases, provider/entity modelling, application-owned confidence, and a reviewed corpus.

Elasticsearch's own phonetic-filter guidance recommends keeping ordinary and phonetic terms in separate fields with different boosts. That principle should be part of our engine-neutral search-document model.

Official references:

- <https://www.meilisearch.com/docs/capabilities/full_text_search/relevancy/typo_tolerance_settings>
- <https://typesense.org/docs/latest/api/search.html>
- <https://www.elastic.co/docs/reference/elasticsearch/plugins/analysis-phonetic-token-filter>
- <https://docs.opensearch.org/latest/query-dsl/term/fuzzy/>

## Speech-pipeline finding

The current Home Assistant STT contract returns a `SpeechResult` containing only `text` and result state. It does not expose N-best alternatives or recognition confidence through the standard entity contract. The MCP request can remain extensible for callers that do provide alternatives or confidence, but the initial Home Assistant path cannot depend on them.

Official references:

- <https://developers.home-assistant.io/docs/core/entity/stt/>
- <https://github.com/home-assistant/core/blob/dev/homeassistant/components/stt/models.py>

## Engine-neutral contracts

The code should keep these responsibilities separate:

- `ICatalogueLifecycleService` and `ICatalogueImportWriter`: canonical LMS entities, relationships, provider metadata, refresh state, and bounded ingestion.
- `ISearchDocumentFactory`: derives versioned exact, normalized, n-gram, phonetic, alias, and future embedding fields from catalogue entities.
- `IMediaSearchIndex`: builds or updates a generation and returns candidate ids with backend evidence.
- `IMediaResolver`: merges candidate lanes, applies domain/context ranking, records the attempt, and produces calibrated confidence.
- `IResolutionAttemptStore`: persists inputs, candidates, feature contributions, timings, versions, correlations, and human review.

The index is derived data. It must be completely rebuildable from the canonical catalogue and alias/pronunciation records. Index generations should be versioned and swapped atomically so algorithm experiments and upgrades do not corrupt the live resolver.

Backend-specific query objects, scores, and identifiers must stop at `IMediaSearchIndex`. MCP result schemas should contain only product-level media references, confidence, alternatives, and explainable match evidence.

## Benchmark gate before selection

Build the same `SearchDocument` corpus and resolver contract over at least these candidates:

1. A custom in-memory multi-map/n-gram/edit-distance baseline.
2. SQLite FTS5 with generated normalized, alias, and phonetic fields plus application reranking.
3. Lucene.NET with separate exact, normalized, n-gram, and phonetic fields.

Use PostgreSQL as a comparison when the cost of running one test service is acceptable. A Meilisearch or Typesense run can provide a useful turnkey typo-tolerance baseline.

Evaluate against private representative catalogues at multiple scales and larger synthetic catalogues. The labelled query set should cover:

- Exact and normalized queries.
- Single and multiple spelling edits.
- Speech-like substitutions and homophones.
- Acronyms and stylised names, including fictional cases such as `ZYRAQ` → `zyrack`.
- Diacritics, transliterations, and non-English names.
- Split/joined words, punctuation, featured artists, and changed word order.
- Partial artist/title/album combinations.
- Short names and ambiguous names.
- Queries for which no item should match.

Measure:

- Recall at 1, 3, and 5; mean reciprocal rank.
- False-confident-match rate and clarification rate.
- Confidence calibration by band and score margin.
- Warm and cold p50, p95, and p99 lookup latency.
- Full and incremental index time, memory, disk, and index-swap time.
- Behaviour after catalogue changes and process restart.

Do not select a backend solely because it makes one stylised-name case work or because its average latency is low. Selection requires good results across the labelled error categories, predictable tail latency, inspectable scoring, simple open-source deployment, and a safe index lifecycle.

## Current recommendation

- Commit to the engine-neutral boundaries now.
- Do not commit to SQLite FTS5, Lucene.NET, PostgreSQL, or a search service until the benchmark spike.
- It remains reasonable to use SQLite provisionally for application state and search-review history, provided search is a replaceable derived subsystem.
- Make aliases/pronunciations and reviewed corrections portable canonical data, not backend-specific tokenizer configuration.
- Start with lexical, n-gram, edit-distance, phonetic-key, and alias lanes. Defer semantic vectors and learned phoneme models until the labelled corpus shows they solve failures the simpler lanes cannot.
