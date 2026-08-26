<template>
  <main class="detail-page">
    <RouterLink class="back" :to="{ name: 'search-observations' }">← Observation log</RouterLink>
    <p v-if="observations.errorMessage" class="error" role="alert">{{ observations.errorMessage }}</p>
    <div v-else-if="observations.loading || !item" class="loading">Loading observation…</div>
    <template v-else>
      <header class="detail-heading">
        <div>
          <p class="eyebrow">{{ formatDate(item.createdAt) }}</p>
          <h1>{{ observationTitle }}</h1>
          <p v-if="item.normalisedQuery !== item.originalQuery">Normalised to “{{ item.normalisedQuery }}”</p>
          <p v-if="item.rating !== null">
            Rating {{ item.ratingMatch === 'at_least' ? 'at least' : 'exactly' }} {{ item.rating }}
          </p>
          <p v-if="item.genre">Genre “{{ item.genre }}”</p>
          <p v-if="item.effectiveFromYear !== null">
            Years {{ item.effectiveFromYear }}–{{ item.effectiveToYear }}
            <template v-if="item.requestedFromYear !== item.effectiveFromYear || item.requestedToYear !== item.effectiveToYear">
              (requested {{ item.requestedFromYear }}–{{ item.requestedToYear }})
            </template>
          </p>
        </div>
        <span class="resolver">{{ item.resolver }} · v{{ item.resolverVersion }}</span>
      </header>

      <section class="timings" aria-label="Search timings">
        <div><strong>{{ item.totalDurationMilliseconds }} ms</strong><span>Total</span></div>
        <div><strong>{{ item.retrievalDurationMilliseconds }} ms</strong><span>Candidate retrieval</span></div>
        <div><strong>{{ item.processingDurationMilliseconds }} ms</strong><span>Result processing</span></div>
        <div><strong>{{ item.candidates.length }}</strong><span>Candidates</span></div>
      </section>

      <aside v-if="item.status === 'failed'" class="failure-notice" role="status">
        <strong>Search request failed</strong>
        <span>{{ item.failureMessage ?? 'A search source did not complete.' }}</span>
        <span>Any candidates below came from sources that completed successfully.</span>
      </aside>

      <section class="panel">
        <div class="panel-title"><h2>Retrieval sources</h2><span>{{ item.provider }} · {{ item.collection.replaceAll('_', ' ') }}</span></div>
        <div v-if="!item.requests.length" class="empty-inline">No retrieval source was captured.</div>
        <article v-for="request in item.requests" :key="request.source" class="request-row">
          <div>
            <strong>{{ request.source }}</strong>
            <span :class="{ 'request-failed': request.status === 'failed' }">
              {{ request.status }} · {{ request.resultCount }} results · {{ request.durationMilliseconds }} ms
            </span>
            <span v-if="request.failureMessage" class="request-failed">{{ request.failureMessage }}</span>
          </div>
          <code>{{ request.command }}</code>
        </article>
      </section>

      <section class="panel">
        <div class="panel-title"><h2>Ordered candidates</h2></div>
        <div v-if="!item.candidates.length" class="no-results">
          {{ item.status === 'failed' ? 'No candidates were recovered before the request failed.' : 'Search returned no candidates.' }}
        </div>
        <ol v-else class="candidate-list">
          <li v-for="candidate in item.candidates" :key="candidate.correlationId" :class="{ selected: candidate.selectedAt }">
            <span class="position">{{ candidate.position }}</span>
            <div><strong>{{ candidate.title }}</strong><span>{{ candidate.kind }}<template v-if="candidate.isExactArtistMatch"> · exact artist match</template><template v-if="candidate.artist"> · {{ candidate.artist }}</template><template v-if="candidate.album"> · {{ candidate.album }}</template><template v-if="candidate.rating !== null"> · rating {{ candidate.rating }}</template></span></div>
            <span v-if="candidate.selectedAt" class="played">Played</span>
          </li>
        </ol>
      </section>

      <form class="panel review-form" @submit.prevent="save">
        <div class="panel-title"><h2>Human review</h2></div>
        <div class="form-grid">
          <label>Classification
            <select v-model="classification" required>
              <option value="good">Good result</option><option value="wrong_order">Wrong order</option>
              <option value="no_match">No match</option><option value="ambiguous">Ambiguous</option>
              <option value="transcription_error">Transcription error</option><option value="other">Other</option>
            </select>
          </label>
          <label>Expected returned candidate
            <select v-model="expectedCorrelationId">
              <option value="">None / enter expected result below</option>
              <option v-for="candidate in item.candidates" :key="candidate.correlationId" :value="candidate.correlationId">#{{ candidate.position }} · {{ candidate.title }}</option>
            </select>
          </label>
          <label>Expected kind
            <select v-model="expectedKind">
              <option value="">Not specified</option><option value="artist">Artist</option>
              <option value="album">Album</option><option value="track">Track</option><option value="playlist">Playlist</option>
            </select>
          </label>
          <label>Expected title<input v-model="expectedTitle"></label>
          <label>Expected artist<input v-model="expectedArtist"></label>
          <label>Expected album<input v-model="expectedAlbum"></label>
        </div>
        <label>Notes<textarea v-model="notes" rows="3" placeholder="Why was this result good or bad?"></textarea></label>
        <label class="check" :class="{ disabled: item.status === 'failed' }">
          <input v-model="includeInEvaluation" type="checkbox" :disabled="item.status === 'failed'">
          {{ item.status === 'failed' ? 'Failed requests cannot enter the evaluation corpus' : 'Include this reviewed case in anonymised evaluation exports' }}
        </label>
        <div class="save-row"><button type="submit" :disabled="observations.saving">{{ observations.saving ? 'Saving…' : 'Save review' }}</button><span v-if="saved" role="status">Saved</span></div>
      </form>

      <aside class="privacy">Stored locally for {{ item.retentionDays }} days. Exports omit observation IDs, LMS media IDs, correlation references, timestamps and private review notes.</aside>
    </template>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import type { SearchClassification } from './searchObservationsApi';
import { useSearchObservationsStore } from './searchObservationsStore';

const route = useRoute();
const observations = useSearchObservationsStore();
const item = computed(() => observations.selected);
const observationTitle = computed(() => {
  if (!item.value) return '';
  if (item.value.interpretation === 'broad_discovery') return 'Broad discovery';
  if (item.value.interpretation === 'name_free_filtered') return 'Name-free filtered search';
  return item.value.originalQuery ? `“${item.value.originalQuery}”` : 'Constraint-only search';
});
const classification = ref<SearchClassification>('good');
const expectedCorrelationId = ref('');
const expectedKind = ref('');
const expectedTitle = ref('');
const expectedArtist = ref('');
const expectedAlbum = ref('');
const notes = ref('');
const includeInEvaluation = ref(false);
const saved = ref(false);

watch(item, value => {
  if (!value) return;
  classification.value = value.review?.classification ?? defaultClassification(value);
  expectedCorrelationId.value = value.review?.expectedCorrelationId ?? '';
  expectedKind.value = value.review?.expectedKind ?? '';
  expectedTitle.value = value.review?.expectedTitle ?? '';
  expectedArtist.value = value.review?.expectedArtist ?? '';
  expectedAlbum.value = value.review?.expectedAlbum ?? '';
  notes.value = value.review?.notes ?? '';
  includeInEvaluation.value = value.status === 'completed' && (value.review?.includeInEvaluation ?? false);
}, { immediate: true });

onMounted(async () => observations.load(String(route.params.id)));

async function save(): Promise<void> {
  const success = await observations.save(String(route.params.id), {
    classification: classification.value,
    expectedCorrelationId: expectedCorrelationId.value || null,
    expectedKind: expectedKind.value || null,
    expectedTitle: expectedTitle.value || null,
    expectedArtist: expectedArtist.value || null,
    expectedAlbum: expectedAlbum.value || null,
    notes: notes.value || null,
    includeInEvaluation: includeInEvaluation.value
  });
  saved.value = success;
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'full', timeStyle: 'medium' }).format(new Date(value));
}

function defaultClassification(value: { status: string; candidates: unknown[] }): SearchClassification {
  if (value.status === 'failed') return 'other';
  return value.candidates.length > 0 ? 'good' : 'no_match';
}
</script>

<style scoped>
.detail-page { width: min(1050px, calc(100% - 40px)); margin: 0 auto; padding: 42px 0 64px; }
.back { display: inline-block; margin-bottom: 34px; color: var(--text-muted); text-decoration: none; }
.back:hover { color: var(--accent); }
.detail-heading { display: flex; justify-content: space-between; align-items: start; gap: 24px; margin-bottom: 28px; }
.eyebrow { color: var(--accent); font-size: .75rem; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; }
h1 { margin: 0 0 8px; font: 620 clamp(2rem, 5vw, 3.8rem)/1 var(--font-display); letter-spacing: -.04em; }
.detail-heading p { margin: 0 0 7px; color: var(--text-muted); }
.resolver { padding: 8px 11px; border: 1px solid var(--border); border-radius: 999px; color: var(--accent-soft); font-size: .78rem; white-space: nowrap; }
.timings { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin-bottom: 18px; }
.timings div, .panel { border: 1px solid var(--border); background: var(--surface); }
.timings div { padding: 17px; border-radius: 13px; }
.timings strong, .timings span { display: block; }
.timings strong { margin-bottom: 4px; font-size: 1.25rem; }
.failure-notice { display: grid; gap: 5px; margin-bottom: 16px; padding: 16px 18px; border: 1px solid rgba(255,146,146,.3); border-radius: 13px; color: var(--danger-text); background: rgba(255,146,146,.06); }
.failure-notice span { font-size: .84rem; }
.timings span, .panel-title span, .request-row span, .candidate-list li div span { color: var(--text-muted); font-size: .78rem; }
.panel { margin-bottom: 16px; padding: 22px; border-radius: 16px; }
.panel-title { display: flex; justify-content: space-between; gap: 18px; align-items: baseline; margin-bottom: 17px; }
h2 { margin: 0; font-size: 1.05rem; }
.request-row { display: grid; grid-template-columns: 150px 1fr; gap: 16px; padding: 12px 0; border-top: 1px solid var(--border); }
.request-row div span, .candidate-list li div span { display: block; margin-top: 4px; }
.request-failed { color: var(--danger-text) !important; }
code { overflow-x: auto; padding: 9px 11px; border-radius: 7px; color: var(--accent-soft); background: rgba(0,0,0,.25); font-size: .78rem; }
.candidate-list { margin: 0; padding: 0; list-style: none; }
.candidate-list li { display: grid; grid-template-columns: 34px 1fr auto; gap: 12px; align-items: center; padding: 13px 0; border-top: 1px solid var(--border); }
.candidate-list li.selected { color: var(--success); }
.position { color: var(--text-dim); font-variant-numeric: tabular-nums; }
.played { font-size: .72rem; font-weight: 700; text-transform: uppercase; }
.no-results, .empty-inline, .privacy { color: var(--text-muted); }
.no-results { padding: 26px; border: 1px dashed rgba(255,146,146,.35); border-radius: 10px; color: var(--danger-text); text-align: center; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 13px; margin-bottom: 13px; }
label { display: grid; gap: 7px; color: var(--text-muted); font-size: .8rem; font-weight: 700; }
input, select, textarea { width: 100%; padding: 10px 11px; border: 1px solid var(--border); border-radius: 8px; color: var(--text); background: #211e19; font: inherit; }
.check { display: flex; align-items: center; margin: 16px 0; font-weight: 500; }
.check input { width: auto; }
.check.disabled { color: var(--text-dim); }
.save-row { display: flex; align-items: center; gap: 12px; }
button { padding: 11px 15px; border: 1px solid var(--accent); border-radius: 9px; color: #21170a; background: var(--accent); font: inherit; font-weight: 700; cursor: pointer; }
.save-row span { color: var(--success); }
.privacy { padding: 12px 2px; font-size: .78rem; line-height: 1.5; }
.error { color: var(--danger-text); }.loading { color: var(--text-muted); }
@media (max-width: 700px) { .detail-heading { flex-direction: column; } .timings { grid-template-columns: repeat(2,1fr); } .form-grid { grid-template-columns: 1fr; } .request-row { grid-template-columns: 1fr; } }
</style>
