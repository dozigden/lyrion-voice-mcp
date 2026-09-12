<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useToolLogStore } from './toolLogStore';
import ToolCallDetail from './ToolCallDetail.vue';
import ToolIcon from './components/ToolIcon.vue';
import { formatDate, callTime, callDay, label, outcomeSymbol } from '../../shared/format';
const route = useRoute(), router = useRouter(), log = useToolLogStore();
const tools = ['search', 'browse', 'get_player_status', 'control_player', 'get_queue', 'manage_queue', 'play'];
const statuses = ['running', 'succeeded', 'tool_error', 'cancelled', 'failed', 'interrupted'];
const listElement = ref<HTMLElement | null>(null);
const detailElement = ref<HTMLElement | null>(null);
const tool = ref(''), status = ref('');
const toolOptions = computed(() => [...new Set([...tools, tool.value])].filter(Boolean));
const statusOptions = computed(() => [...new Set([...statuses, status.value])].filter(Boolean));
const selectedId = computed(() => typeof route.params.id === 'string' ? route.params.id : '');
const mobileDetail = computed(() => !!selectedId.value && route.query.view !== 'list');
const offset = computed(() => {
  const value = Number(route.query.offset ?? 0);
  return Number.isSafeInteger(value) && value >= 0 ? value : 0;
});
const query = computed(() => {
  const params = new URLSearchParams({ offset: String(offset.value), limit: '50' });
  if (typeof route.query.toolName === 'string' && route.query.toolName) params.set('toolName', route.query.toolName);
  if (typeof route.query.status === 'string' && route.query.status) params.set('status', route.query.status);
  return `?${params}`;
});
const first = computed(() => log.page?.items.length ? log.page.offset + 1 : 0);
const last = computed(() => log.page ? log.page.offset + log.page.items.length : 0);
let alive = true;
let loadedQuery = '';
async function loadPage() {
  const requestedQuery = query.value;
  const page = await log.loadList(requestedQuery);
  if (!alive || !page || requestedQuery !== query.value) return;
  loadedQuery = requestedQuery;
  if (!selectedId.value && page.items[0]) {
    await router.replace({ name: 'tool-calls-detail', params: { id: page.items[0].id }, query: { ...route.query, view: 'list' } });
  }
  await nextTick();
  if (listElement.value) listElement.value.scrollTop = log.scrollTop;
}
watch(query, () => {
  tool.value = typeof route.query.toolName === 'string' ? route.query.toolName : '';
  status.value = typeof route.query.status === 'string' ? route.query.status : '';
  void loadPage();
}, { immediate: true });
watch([tool, status], () => {
  const routeTool = typeof route.query.toolName === 'string' ? route.query.toolName : '';
  const routeStatus = typeof route.query.status === 'string' ? route.query.status : '';
  if (tool.value === routeTool && status.value === routeStatus) return;
  log.scrollTop = 0;
  void router.replace({
    name: 'tool-calls',
    query: { toolName: tool.value || undefined, status: status.value || undefined }
  });
});
watch(selectedId, async id => {
  if (id) await log.loadDetail(id);
  else {
    log.cancelDetail();
    if (!log.listLoading && loadedQuery === query.value && log.page?.items[0]) {
      await router.replace({ name: 'tool-calls-detail', params: { id: log.page.items[0].id }, query: { ...route.query, view: 'list' } });
    }
  }
  if (alive && detailElement.value) detailElement.value.scrollTop = 0;
}, { immediate: true });
onBeforeUnmount(() => { alive = false; log.cancel(); });
function rememberScroll() { log.scrollTop = listElement.value?.scrollTop ?? 0; }
function selectCall(id: string) {
  rememberScroll();
  if (id === selectedId.value) void log.loadDetail(id);
  const next = { ...route.query }; delete next.view;
  void router.push({ name: 'tool-calls-detail', params: { id }, query: next });
}
async function returnToList() {
  await router.push({ query: { ...route.query, view: 'list' } });
  nextTick(() => listElement.value?.querySelector<HTMLElement>('[aria-current="true"]')?.focus({ preventScroll: true }));
}
function changePage(nextOffset: number) {
  log.scrollTop = 0;
  void router.push({ name: 'tool-calls', query: { toolName: tool.value || undefined, status: status.value || undefined, offset: nextOffset || undefined } });
}
function previousPage() { changePage(Math.max(0, offset.value - 50)); }
function nextPage() { changePage(offset.value + 50); }
function retryList() { void loadPage(); }
function retryDetail() { if (selectedId.value) void log.loadDetail(selectedId.value); }
</script>
<template>
  <main class="workspace" :class="{ 'mobile-detail': mobileDetail }">
    <aside class="log-pane" aria-label="MCP calls">
      <header class="log-header"><div class="log-title"><h1>Tool log</h1><span class="muted">Newest first</span></div>
        <form class="filters" @submit.prevent>
          <label>Tool<select v-model="tool" aria-label="Tool"><option value="">All tools</option><option v-for="name in toolOptions" :key="name" :value="name">{{ name }}</option></select></label>
          <label>Outcome<select v-model="status" aria-label="Outcome"><option value="">All outcomes</option><option v-for="value in statusOptions" :key="value" :value="value">{{ label(value) }}</option></select></label>
        </form>
      </header>
      <div ref="listElement" class="log-list" :aria-busy="log.listLoading" @scroll="rememberScroll">
        <p v-if="log.listError" class="list-message error" role="alert">{{ log.listError }} <button @click="retryList">Retry</button></p>
        <p v-else-if="log.listLoading" class="list-message muted" role="status">Loading calls…</p>
        <p v-else-if="!log.page?.items.length" class="list-message muted">No calls match these filters.</p>
        <button v-for="call in log.page?.items ?? []" :key="call.id" class="call-row" :class="{ selected: call.id === selectedId }" :aria-current="call.id === selectedId" @click="selectCall(call.id)">
          <span class="row-top"><strong><ToolIcon :tool="call.toolName" />{{ call.toolName }}</strong><time :datetime="call.startedAt" :title="formatDate(call.startedAt)"><span v-if="callDay(call.startedAt)">{{ callDay(call.startedAt) }} · </span>{{ callTime(call.startedAt) }}</time></span>
          <span v-if="call.requestSummary" class="row-summary" :title="call.requestSummary">{{ call.requestSummary }}</span>
          <span v-if="call.status !== 'succeeded'" class="row-outcome" :class="{ danger: ['failed', 'tool_error', 'interrupted'].includes(call.status) }"><span aria-hidden="true" class="outcome-symbol">{{ outcomeSymbol(call.status) }}</span> {{ label(call.status) }}</span>
        </button>
      </div>
      <nav class="pagination" aria-label="Tool log pages"><span>{{ first }}–{{ last }} of {{ log.page?.total ?? 0 }} calls</span><div><button aria-label="Previous page" :disabled="log.listLoading || offset === 0" @click="previousPage">←</button><button aria-label="Next page" :disabled="log.listLoading || !log.page || offset + 50 >= log.page.total" @click="nextPage">→</button></div></nav>
    </aside>
    <section ref="detailElement" class="detail-pane" aria-label="Selected call" :aria-busy="log.detailLoading">
      <button class="back-to-list" @click="returnToList">← Tool log</button>
      <div v-if="log.detailError && selectedId" class="detail-message error" role="alert">{{ log.detailError }} <button @click="retryDetail">Retry</button></div>
      <p v-else-if="log.detailLoading && selectedId" class="detail-message muted" role="status">Loading call…</p>
      <ToolCallDetail v-else-if="selectedId && log.call?.id === selectedId" :call="log.call" />
      <p v-else class="detail-message muted">Select a call to inspect its request and result.</p>
    </section>
  </main>
</template>
<style scoped>
.workspace { flex:1; display:grid; grid-template-columns:355px minmax(0,1fr); min-height:0; }
.log-pane { background:var(--sidebar); display:flex; flex-direction:column; min-height:0; border-right:1px solid var(--border); }
.log-header { padding:14px 20px 12px; }.log-title { display:flex; justify-content:space-between; align-items:baseline; gap:12px; margin-bottom:10px; } h1 { margin:0; font-size:22px; }.log-title span { font-size:14px; }
.filters { display:grid; grid-template-columns:minmax(0,1fr); gap:10px; } label { display:grid; gap:5px; min-width:0; font-size:14px; color:var(--text-muted); } select { width:100%; min-width:0; font-size:14px; padding:7px 9px; }
.log-list { overflow:auto; flex:1; border-top:1px solid var(--border); scrollbar-width:thin; }.list-message { padding:12px 20px; }
.call-row { display:block; width:100%; text-align:left; border:0; border-left:3px solid transparent; border-bottom:1px solid var(--border); border-radius:0; background:transparent; padding:15px 19px 14px; }
.call-row:hover { background:var(--selection-hover); }.call-row.selected { background:var(--selection); color:#fff7ef; border-left-color:#321d2c; }.row-top { display:flex; justify-content:space-between; align-items:start; gap:10px; }.row-top strong { display:flex; align-items:center; gap:8px; min-width:0; font:600 15px/1.4 ui-monospace,monospace; overflow-wrap:anywhere; }.row-top .tool-icon { font-size:19px; }.row-top time { font-size:14px; color:var(--text-muted); text-align:right; max-width:125px; white-space:nowrap; flex-shrink:0; }.row-outcome.danger { color:var(--danger-text); }.selected .row-outcome.danger { color:#ffd7c9; }.outcome-symbol { color:var(--success); }.selected .outcome-symbol { color:#d2e5d5; }.danger .outcome-symbol { color:inherit; }.row-outcome { display:block; margin-top:8px; font-size:14px; color:var(--text-muted); }.selected time,.selected .row-outcome { color:#f0e2eb; }.call-row:focus-visible { outline-offset:-3px; outline-color:#c89bbd; }
.row-summary { display:-webkit-box; -webkit-box-orient:vertical; -webkit-line-clamp:2; line-clamp:2; overflow:hidden; overflow-wrap:anywhere; text-align:left; font-size:14px; line-height:1.45; margin-top:8px; color:var(--text-muted); }.selected .row-summary { color:#f0e2eb; }
.pagination { display:flex; justify-content:space-between; align-items:center; padding:12px 20px; border-top:1px solid var(--border); font-size:14px; }.pagination div { display:flex; gap:14px; }.pagination button { border:0; background:transparent; padding:3px; font-size:20px; }
.detail-pane { --detail-inset:34px; display:flex; flex-direction:column; min-height:0; min-width:0; background:var(--surface); }.detail-message { padding:24px var(--detail-inset); }.back-to-list { display:none; }
@media(min-width:1600px) { .workspace { grid-template-columns:390px minmax(0,1fr); }.detail-pane { --detail-inset:42px; } }
@media(max-width:1050px) { .row-top time > span { display:none; }.workspace { grid-template-columns:295px minmax(0,1fr); }.detail-pane { --detail-inset:24px; } }
@media(max-width:720px) { .row-top time > span { display:inline; }.workspace { display:flex; }.log-pane,.detail-pane { width:100%; }.detail-pane { display:none; --detail-inset:18px; }.mobile-detail .log-pane { display:none; }.mobile-detail .detail-pane { display:flex; }.back-to-list { display:block; border:0; border-radius:0; background:var(--heading-main); color:var(--selection); text-align:left; font-size:14px; padding:10px 18px 0; } }
</style>
