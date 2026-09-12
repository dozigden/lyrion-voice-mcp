<template>
  <main class="page">
    <header class="page-heading">
      <h1>{{ heading }}</h1>
      <p v-if="retentionDays" class="retention">Retained locally for {{ retentionDays }} days.</p>
    </header>
    <form class="filters" :aria-label="`${heading} filters`" @submit.prevent="applyFilters">
      <label>{{ primaryLabel }}
        <input v-model="primaryFilter" type="search" placeholder="All">
      </label>
      <label>{{ kind === 'errors' ? 'Area' : 'Status' }}
        <input v-model="secondaryFilter" type="search" placeholder="All">
      </label>
      <button type="submit" :disabled="loading">Apply filters</button>
    </form>
    <div class="records" role="region" :aria-label="`${heading} records`" tabindex="0">
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <div v-else-if="loading" class="empty">Loading…</div>
      <div v-else-if="!items.length" class="empty">No records match these filters.</div>
      <section v-else class="list">
        <RouterLink v-for="item in items" :key="item.id" class="row" :to="detailLink(item.id)">
          <div><strong>{{ title(item) }}</strong><span>{{ subtitle(item) }}</span></div>
          <div class="signals"><span class="tag" :class="statusClass(recordStatus(item))">{{ recordStatus(item) }}</span><span>{{ time(item) }}</span></div>
        </RouterLink>
      </section>
      <nav v-if="total > limit" class="pagination" aria-label="History pages">
        <button type="button" :disabled="loading || offset === 0" @click="previousPage">Previous</button>
        <span>{{ firstRecord }}–{{ lastRecord }} of {{ total }}</span>
        <button type="button" :disabled="loading || offset + limit >= total" @click="nextPage">Next</button>
      </nav>
    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { storeToRefs } from 'pinia';
import { useHistoryStore, type HistoryKind } from './historyStore';
import type { ErrorLogSummary, JobSummary } from './operationalHistoryApi';
import { formatDate } from '../../shared/format';
const props = defineProps<{ kind: HistoryKind }>();
type OperationalSummary = JobSummary | ErrorLogSummary;
const history = useHistoryStore();
const { listLoading: loading, listError: error } = storeToRefs(history);
const items = computed(() => history.page?.items ?? []);
const total = computed(() => history.page?.total ?? 0);
const retentionDays = computed(() => history.page?.retentionDays);
const offset = ref(0), limit = ref(50);
const primaryFilter = ref(''), secondaryFilter = ref('');
const heading = computed(() => props.kind === 'jobs' ? 'Jobs' : 'Error log');
const primaryLabel = computed(() => props.kind === 'jobs' ? 'Job type' : 'Source');
const firstRecord = computed(() => items.value.length ? offset.value + 1 : 0);
const lastRecord = computed(() => Math.min(offset.value + items.value.length, total.value));
watch(() => props.kind, () => { primaryFilter.value = ''; secondaryFilter.value = ''; offset.value = 0; void load(); }, { immediate: true });
onBeforeUnmount(history.cancelList);
async function load() {
  const query = new URLSearchParams({ offset: String(offset.value), limit: String(limit.value) });
  if (primaryFilter.value.trim()) query.set(props.kind === 'jobs' ? 'type' : 'source', primaryFilter.value.trim());
  if (secondaryFilter.value.trim()) query.set(props.kind === 'errors' ? 'area' : 'status', secondaryFilter.value.trim());
  await history.loadList(props.kind, `?${query}`);
}
function applyFilters() { offset.value = 0; void load(); }
function previousPage() { offset.value = Math.max(0, offset.value - limit.value); void load(); }
function nextPage() { if (offset.value + limit.value < total.value) { offset.value += limit.value; void load(); } }
function detailLink(id: number) { return { name: `${props.kind}-detail`, params: { id: String(id) } }; }
function isJob(item: OperationalSummary): item is JobSummary { return 'type' in item; }
function title(item: OperationalSummary) { return isJob(item) ? `#${item.id} · ${item.type}` : `#${item.id} · ${item.exceptionType}`; }
function subtitle(item: OperationalSummary) { return isJob(item) ? item.correlationId ?? 'No correlation' : `${item.source} · ${item.area} · ${item.message}`; }
function recordStatus(item: OperationalSummary) { return isJob(item) ? item.status : item.area; }
function time(item: OperationalSummary) { return formatDate(isJob(item) ? item.createdAt : item.occurredAt); }
function statusClass(status: string) { return { danger: ['failed', 'interrupted'].includes(status), success: ['completed', 'succeeded'].includes(status) }; }
</script>
<style scoped>
.page { width:min(1200px,100%); margin:0 auto; padding:22px 34px 32px; display:flex; flex-direction:column; flex:1; min-height:0; }
.page-heading { display:flex; flex-shrink:0; align-items:baseline; flex-wrap:wrap; gap:4px 20px; margin-bottom:20px; }
.records { flex:1; min-height:0; overflow:auto; padding:5px; margin:-5px; }
h1 { margin:0; font-size:22px; }
.retention { font-size:14px; color:var(--text-muted); margin:0; }
.filters { display:grid; flex-shrink:0; grid-template-columns:minmax(0,1fr) minmax(140px,220px) auto; align-items:end; gap:12px 16px; padding:12px 16px; margin-bottom:16px; background:var(--heading-band); color:var(--selection); }
.filters label { display:grid; gap:4px; min-width:0; font-size:14px; }
.filters input { width:100%; min-width:0; color:var(--text); }
.filters input,.filters button { padding:6px 12px; font-size:14px; }
.filters button { background:transparent; border-color:var(--selection); }
.list { border-top:1px solid var(--border); }.row { display:flex; justify-content:space-between; gap:24px; padding:16px 18px; border-bottom:1px solid var(--border); color:var(--text); text-decoration:none; }.row:nth-child(even) { background:var(--stripe); }.row:hover { background:var(--selection-hover); }.row strong,.row span { display:block; }.row div>span { margin-top:5px; font-size:14px; color:var(--text-muted); }.row div { min-width:0; overflow-wrap:anywhere; }.signals { flex-shrink:0; text-align:right; }.signals .danger { color:var(--danger-text); }.signals .success { color:var(--success); }
.pagination { display:flex; align-items:center; justify-content:flex-end; gap:16px; margin-top:20px; font-size:14px; }.empty { padding:40px 0; color:var(--text-muted); }
@media(max-width:720px) {
  .page { padding:18px 18px 24px; }
  .page-heading { margin-bottom:16px; }
  .filters { grid-template-columns:minmax(0,1fr) minmax(0,1fr); padding:12px; gap:12px; }
  .filters button { grid-column:1/-1; justify-self:end; }
  .row { flex-direction:column; gap:8px; }
  .signals { text-align:left; }
  .pagination { justify-content:space-between; }
}
@media(max-height:520px) { .page { flex:none; }.records { flex:none; overflow:visible; } }
</style>
