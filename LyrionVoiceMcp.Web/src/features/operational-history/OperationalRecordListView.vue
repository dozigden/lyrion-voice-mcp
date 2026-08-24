<template>
  <main class="page">
    <header><h1>{{ heading }}</h1></header>
    <p v-if="retentionDays" class="retention">Retained locally for {{ retentionDays }} days.</p>
    <form class="filters" @submit.prevent="applyFilters">
      <label>{{ kind === 'jobs' ? 'Job type' : kind === 'errors' ? 'Source' : 'Tool name' }}
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
import { computed, onMounted, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { listErrors, listJobs, listToolCalls, type ErrorLogSummary, type JobSummary, type ToolCallSummary } from './operationalHistoryApi';

const props = defineProps<{ kind: 'jobs' | 'errors' | 'tool-calls' }>();
type OperationalSummary = JobSummary | ErrorLogSummary | ToolCallSummary;
const items = ref<OperationalSummary[]>([]);
const loading = ref(false); const error = ref<string | null>(null); const retentionDays = ref<number | null>(null);
const total = ref(0); const offset = ref(0); const limit = ref(50);
const primaryFilter = ref(''); const secondaryFilter = ref('');
const heading = computed(() => props.kind === 'jobs' ? 'Jobs' : props.kind === 'errors' ? 'Error log' : 'MCP tool calls');
const firstRecord = computed(() => total.value === 0 ? 0 : offset.value + 1);
const lastRecord = computed(() => Math.min(offset.value + items.value.length, total.value));

watch(() => props.kind, () => { primaryFilter.value = ''; secondaryFilter.value = ''; offset.value = 0; void load(); });
onMounted(load);

async function load(): Promise<void> {
  loading.value = true; error.value = null;
  try {
    const query = queryString();
    if (props.kind === 'jobs') { const page = await listJobs(query); applyPage(page); }
    else if (props.kind === 'errors') { const page = await listErrors(query); applyPage(page); }
    else { const page = await listToolCalls(query); applyPage(page); }
  } catch (reason) { error.value = reason instanceof Error ? reason.message : 'The history could not be loaded.'; }
  finally { loading.value = false; }
}

function queryString(): string {
  const query = new URLSearchParams();
  query.set('offset', String(offset.value)); query.set('limit', String(limit.value));
  if (primaryFilter.value.trim()) query.set(props.kind === 'jobs' ? 'type' : props.kind === 'errors' ? 'source' : 'toolName', primaryFilter.value.trim());
  if (secondaryFilter.value.trim()) query.set(props.kind === 'errors' ? 'area' : 'status', secondaryFilter.value.trim());
  const value = query.toString(); return value ? `?${value}` : '';
}
function applyPage(page: { items: OperationalSummary[]; total: number; offset: number; limit: number; retentionDays: number }): void { items.value = page.items; total.value = page.total; offset.value = page.offset; limit.value = page.limit; retentionDays.value = page.retentionDays; }
function applyFilters(): void { offset.value = 0; void load(); }
function previousPage(): void { offset.value = Math.max(0, offset.value - limit.value); void load(); }
function nextPage(): void { if (offset.value + limit.value < total.value) { offset.value += limit.value; void load(); } }
function detailLink(id: string | number) { return { name: `${props.kind}-detail`, params: { id: String(id) } }; }
function isJob(item: OperationalSummary): item is JobSummary { return 'type' in item; }
function isToolCall(item: OperationalSummary): item is ToolCallSummary { return 'toolName' in item; }
function title(item: OperationalSummary): string { if (isJob(item)) return `#${item.id} · ${item.type}`; if (isToolCall(item)) return item.toolName; return `#${item.id} · ${item.exceptionType}`; }
function subtitle(item: OperationalSummary): string { if (isJob(item)) return item.correlationId ?? 'No correlation'; if (isToolCall(item)) return item.traceIdentifier ?? item.id; return `${item.source} · ${item.area} · ${item.message}`; }
function recordStatus(item: OperationalSummary): string { if (isJob(item) || isToolCall(item)) return item.status; return item.area; }
function time(item: OperationalSummary): string { let value: string; if (isJob(item)) value = item.createdAt; else if (isToolCall(item)) value = item.startedAt; else value = item.occurredAt; return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value)); }
function statusClass(status: string | undefined) { return { danger: status === 'failed' || status === 'tool_error' || status === 'interrupted', success: status === 'completed' || status === 'succeeded' }; }
</script>

<style scoped>
.page { width:min(1180px,calc(100% - 40px)); margin:0 auto; padding:48px 0 64px; } header { margin-bottom:24px; } h1 { margin:0; font:620 clamp(2.2rem,5vw,4rem)/1 var(--font-display); } .retention { margin:-12px 0 20px; color:var(--text-muted); font-size:.8rem; } .filters { display:grid; grid-template-columns:1fr 1fr auto; gap:12px; align-items:end; margin-bottom:22px; } label { display:grid; gap:7px; color:var(--text-muted); font-size:.8rem; font-weight:700; } input,button { padding:11px 12px; border:1px solid var(--border); border-radius:9px; color:var(--text); background:#211e19; font:inherit; } button { cursor:pointer; } button:disabled { cursor:not-allowed; opacity:.45; } .list { overflow:hidden; border:1px solid var(--border); border-radius:16px; background:var(--surface); } .row { display:flex; justify-content:space-between; gap:20px; padding:17px 20px; border-bottom:1px solid var(--border); color:var(--text); text-decoration:none; } .row:last-child{border:0}.row:hover{background:rgba(244,175,65,.055)} .row strong,.row span{display:block}.row div>span{margin-top:5px;color:var(--text-dim);font-size:.78rem}.signals{display:flex;align-items:center;gap:10px;text-align:right}.tag{padding:5px 8px;border-radius:999px;background:rgba(255,255,255,.06);text-transform:capitalize}.danger{color:var(--danger-text)}.success{color:var(--success)}.pagination{display:flex;align-items:center;justify-content:flex-end;gap:14px;margin-top:18px;color:var(--text-muted);font-size:.82rem}.empty{padding:50px;border:1px dashed var(--border-strong);border-radius:16px;color:var(--text-muted);text-align:center}.error{color:var(--danger-text)} @media(max-width:700px){.filters{grid-template-columns:1fr}.row{flex-direction:column}.signals{text-align:left}.pagination{justify-content:space-between}}
</style>
