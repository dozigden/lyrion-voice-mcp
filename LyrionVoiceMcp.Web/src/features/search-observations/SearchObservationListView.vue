<template>
  <main class="review-page">
    <header class="page-heading">
      <div>
        <p class="eyebrow">Search evidence</p>
        <h1>Observation log</h1>
        <p>Inspect what the pass-through resolver asked LMS and what came back.</p>
      </div>
      <a class="export-link" href="/api/search-observations/export">Export evaluation cases</a>
    </header>

    <aside class="privacy-notice">
      Search terms and library metadata may be sensitive. They remain in the local operational database
      for {{ observations.page?.retentionDays ?? 90 }} days; only cases you explicitly include are exported.
    </aside>

    <form class="filters" @submit.prevent="refresh">
      <label>
        Search query
        <input v-model="query" type="search" placeholder="e.g. zyrack">
      </label>
      <label>
        Review
        <select v-model="review">
          <option value="all">All</option>
          <option value="unreviewed">Unreviewed</option>
          <option value="reviewed">Reviewed</option>
        </select>
      </label>
      <label>
        Outcome
        <select v-model="result">
          <option value="all">All</option>
          <option value="no-results">No results</option>
          <option value="selected">Selected later</option>
          <option value="failed">Failed request</option>
        </select>
      </label>
      <button type="submit" :disabled="observations.loading">Apply filters</button>
    </form>

    <p v-if="observations.errorMessage" class="error" role="alert">{{ observations.errorMessage }}</p>
    <div v-else-if="observations.loading" class="empty">Loading observations…</div>
    <div v-else-if="!observations.page?.items.length" class="empty">
      No searches match these filters. New MCP searches will appear here automatically.
    </div>
    <section v-else class="result-list" aria-label="Search observations">
      <RouterLink
        v-for="item in observations.page.items"
        :key="item.id"
        class="result-row"
        :to="{ name: 'search-observation-detail', params: { id: item.id } }"
      >
        <div>
          <span class="query">{{ item.originalQuery }}</span>
          <span class="meta">{{ formatDate(item.createdAt) }} · {{ item.resolver }} v{{ item.resolverVersion }}</span>
        </div>
        <div class="signals">
          <span v-if="item.classification" class="tag">{{ label(item.classification) }}</span>
          <span v-if="item.status === 'failed'" class="tag tag--failed">Failed</span>
          <span v-if="item.selectedPosition" class="tag tag--selected">Played #{{ item.selectedPosition }}</span>
          <span :class="{ 'count--empty': item.resultCount === 0 }">{{ item.resultCount }} results</span>
          <span>{{ item.totalDurationMilliseconds }} ms</span>
        </div>
      </RouterLink>
    </section>
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { useSearchObservationsStore } from './searchObservationsStore';

const observations = useSearchObservationsStore();
const query = ref('');
const review = ref<'all' | 'unreviewed' | 'reviewed'>('all');
const result = ref<'all' | 'no-results' | 'selected' | 'failed'>('all');

onMounted(refresh);

async function refresh(): Promise<void> {
  await observations.browse({ query: query.value, review: review.value, result: result.value });
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}

function label(value: string): string {
  return value.replaceAll('_', ' ');
}
</script>

<style scoped>
.review-page { width: min(1180px, calc(100% - 40px)); margin: 0 auto; padding: 48px 0 64px; }
.page-heading { display: flex; justify-content: space-between; align-items: end; gap: 24px; margin-bottom: 28px; }
.eyebrow { margin: 0 0 8px; color: var(--accent); font-size: .75rem; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; }
h1 { margin: 0 0 8px; font: 620 clamp(2.2rem, 5vw, 4rem)/1 var(--font-display); letter-spacing: -.045em; }
.page-heading p:last-child { margin: 0; color: var(--text-muted); }
.export-link, button { border: 1px solid var(--border-strong); border-radius: 10px; color: var(--text); background: transparent; font: inherit; padding: 11px 14px; text-decoration: none; cursor: pointer; }
.export-link:hover, button:hover { border-color: var(--accent); color: var(--accent); }
.privacy-notice { margin-bottom: 22px; padding: 14px 17px; border-left: 3px solid var(--accent); color: var(--text-muted); background: rgba(244,175,65,.06); line-height: 1.5; }
.filters { display: grid; grid-template-columns: 2fr 1fr 1fr auto; gap: 12px; align-items: end; margin-bottom: 22px; }
label { display: grid; gap: 7px; color: var(--text-muted); font-size: .8rem; font-weight: 700; }
input, select { min-width: 0; padding: 11px 12px; border: 1px solid var(--border); border-radius: 9px; color: var(--text); background: #211e19; font: inherit; }
.result-list { overflow: hidden; border: 1px solid var(--border); border-radius: 16px; background: var(--surface); }
.result-row { display: flex; justify-content: space-between; gap: 24px; padding: 18px 20px; border-bottom: 1px solid var(--border); color: var(--text); text-decoration: none; }
.result-row:last-child { border-bottom: 0; }
.result-row:hover { background: rgba(244,175,65,.055); }
.query { display: block; margin-bottom: 6px; font: 600 1.15rem var(--font-display); }
.meta { color: var(--text-dim); font-size: .78rem; }
.signals { display: flex; flex-wrap: wrap; justify-content: end; align-items: center; gap: 9px; color: var(--text-muted); font-size: .78rem; }
.tag { padding: 5px 8px; border-radius: 999px; background: rgba(255,255,255,.06); text-transform: capitalize; }
.tag--selected { color: var(--success); }
.tag--failed { color: var(--danger-text); }
.count--empty, .error { color: var(--danger-text); }
.empty { padding: 52px 24px; border: 1px dashed var(--border-strong); border-radius: 16px; color: var(--text-muted); text-align: center; }
@media (max-width: 760px) { .page-heading, .result-row { align-items: flex-start; flex-direction: column; } .filters { grid-template-columns: 1fr; } .signals { justify-content: flex-start; } }
</style>
