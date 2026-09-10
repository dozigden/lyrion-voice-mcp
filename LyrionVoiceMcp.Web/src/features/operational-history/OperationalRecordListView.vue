<template>
  <main class="page">
    <header><h1>{{ heading }}</h1></header>
    <p v-if="retentionDays" class="retention">Retained locally for {{ retentionDays }} days.</p>
    <form class="filters" @submit.prevent="applyFilters">
      <label>{{ primaryLabel }}
        <input v-model="primaryFilter" type="search" placeholder="All">
      </label>
      <label>{{ kind === 'errors' ? 'Area' : 'Status' }}
        <input v-model="secondaryFilter" type="search" placeholder="All">
      </label>
      <button type="submit" :disabled="loading">Apply filters</button>
    </form>
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
.page { width:min(1200px,100%); margin:0 auto; padding:28px 34px 40px; } header { margin-bottom:24px; } h1 { margin:0; font-size:22px; }.retention { font-size:14px; color:var(--text-muted); margin:0 0 20px; }
.filters { display:grid; grid-template-columns:1fr 1fr auto; align-items:end; gap:14px; margin-bottom:24px; } label { display:grid; gap:6px; font-size:14px; color:var(--text-muted); }
.list { border-top:1px solid var(--border); }.row { display:flex; justify-content:space-between; gap:24px; padding:16px 18px; border-bottom:1px solid var(--border); color:var(--text); text-decoration:none; }.row:nth-child(even) { background:var(--stripe); }.row:hover { background:var(--selection-hover); }.row strong,.row span { display:block; }.row div>span { margin-top:5px; font-size:14px; color:var(--text-muted); }.row div { min-width:0; overflow-wrap:anywhere; }.signals { flex-shrink:0; text-align:right; }.signals .danger { color:var(--danger-text); }.signals .success { color:var(--success); }
.pagination { display:flex; align-items:center; justify-content:flex-end; gap:16px; margin-top:20px; font-size:14px; }.empty { padding:40px 0; color:var(--text-muted); }
@media(max-width:720px) { .page { padding:24px 18px; }.filters { grid-template-columns:1fr; }.row { flex-direction:column; gap:8px; }.signals { text-align:left; }.pagination { justify-content:space-between; } }
</style>
