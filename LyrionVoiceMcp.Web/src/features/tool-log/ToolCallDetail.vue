<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { ToolCall } from '../operational-history/operationalHistoryApi';
import { presentCall } from './toolResults';
import { duration, formatDate, label, pretty, outcomeSymbol } from '../../shared/format';
import SearchResult from './renderers/SearchResult.vue';
import BrowseResult from './renderers/BrowseResult.vue';
import PlayerStatusResult from './renderers/PlayerStatusResult.vue';
import ControlPlayerResult from './renderers/ControlPlayerResult.vue';
import QueueResult from './renderers/QueueResult.vue';
import ManageQueueResult from './renderers/ManageQueueResult.vue';
import PlayResult from './renderers/PlayResult.vue';
import RecordedValue from './components/RecordedValue.vue';
const props = defineProps<{ call: ToolCall }>();
const tab = ref<'details' | 'raw'>('details');
const presentation = computed(() => presentCall(props.call));
const result = computed(() => presentation.value.result);
const requestLabels: Record<string, string> = { name: 'Name', genre: 'Genre', fromYear: 'From year', toYear: 'To year', rating: 'Rating', ratingMatch: 'Rating match', player: 'Player', action: 'Action', items: 'Item references', browseRef: 'Browse reference' };
watch(() => props.call.id, () => { tab.value = 'details'; });
function showDetails() { tab.value = 'details'; }
function showRaw() { tab.value = 'raw'; }
function switchTab(event: KeyboardEvent) {
  if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
    event.preventDefault();
    if (event.key === 'Home') tab.value = 'details';
    else if (event.key === 'End') tab.value = 'raw';
    else tab.value = tab.value === 'details' ? 'raw' : 'details';
    document.getElementById(`call-tab-${tab.value}`)?.focus();
  }
}
</script>
<template>
  <header class="call-heading"><div><h2>{{ call.toolName }}</h2><span><span aria-hidden="true" class="outcome-symbol">{{ outcomeSymbol(call.status) }}</span> {{ label(call.status) }}</span></div><div class="timing"><time :datetime="call.startedAt">{{ formatDate(call.startedAt) }}</time><span>{{ duration(call.durationMilliseconds) }}</span></div></header>
  <div class="tabs" role="tablist" aria-label="Call presentation" @keydown="switchTab">
    <button id="call-tab-details" role="tab" :aria-selected="tab === 'details'" :tabindex="tab === 'details' ? 0 : -1" aria-controls="call-panel" @click="showDetails">Details</button>
    <button id="call-tab-raw" role="tab" :aria-selected="tab === 'raw'" :tabindex="tab === 'raw' ? 0 : -1" aria-controls="call-panel" @click="showRaw">Raw JSON</button>
  </div>
  <div id="call-panel" class="detail-scroll" role="tabpanel" :aria-labelledby="`call-tab-${tab}`" tabindex="0">
    <div v-if="call.argumentsTruncated || call.resultTruncated" class="inset"><p class="notice">This recording was truncated before storage. Raw JSON contains only the saved portion.</p></div>
    <template v-if="tab === 'details'">
      <section class="request inset" aria-label="Request"><h3>Arguments</h3><p v-if="presentation.requestUnavailable" class="muted">The request cannot be fully presented. Inspect Raw JSON.</p><p v-else-if="!presentation.requests.length" class="muted">No arguments supplied.</p><dl v-else><div v-for="field in presentation.requests" :key="field.key"><dt>{{ requestLabels[field.key] ?? field.key }}</dt><dd><RecordedValue :value="field.value" /></dd></div></dl></section>
      <div v-if="call.errorMessage || presentation.isError || presentation.messages.length" class="inset">
        <p v-if="call.errorMessage" class="notice error">{{ call.errorMessage }}</p>
        <p v-else-if="presentation.isError" class="notice error">The tool returned an error.</p>
        <p v-for="(message, index) in presentation.messages" :key="index" class="message">{{ message }}</p>
      </div>
      <section v-for="(message, index) in presentation.structuredMessages" :key="index" class="inset structured-message"><h3>Response message</h3><RecordedValue :value="message" /></section>
      <SearchResult v-if="result?.tool === 'search'" :result="result.data" />
      <BrowseResult v-else-if="result?.tool === 'browse'" :result="result.data" />
      <PlayerStatusResult v-else-if="result?.tool === 'get_player_status'" :result="result.data" />
      <ControlPlayerResult v-else-if="result?.tool === 'control_player'" :result="result.data" />
      <QueueResult v-else-if="result?.tool === 'get_queue'" :result="result.data" />
      <ManageQueueResult v-else-if="result?.tool === 'manage_queue'" :result="result.data" />
      <PlayResult v-else-if="result?.tool === 'play'" :result="result.data" />
      <div class="inset">
        <p v-if="call.resultJson === null" class="muted">No result recorded.</p>
        <p v-else-if="presentation.fallback" class="notice">This saved result does not match a supported presentation. <button class="text-button" @click="showRaw">Inspect Raw JSON</button></p>
        <p v-if="presentation.hasOtherContent" class="notice">Additional non-text content is available in Raw JSON.</p>
        <details v-if="presentation.references.length" class="technical"><summary>References and response guidance</summary><dl><div v-for="entry in presentation.references" :key="entry.path"><dt>{{ entry.path }}</dt><dd>{{ entry.value }}</dd></div></dl></details>
        <details class="technical"><summary>Call diagnostics</summary><dl><div><dt>Call ID</dt><dd>{{ call.id }}</dd></div><div><dt>Completed</dt><dd>{{ formatDate(call.completedAt) }}</dd></div><div><dt>Trace</dt><dd>{{ call.traceIdentifier ?? 'Not recorded' }}</dd></div><div v-if="call.errorLogId !== null"><dt>Error log</dt><dd><RouterLink :to="{ name: 'errors-detail', params: { id: call.errorLogId } }">Error #{{ call.errorLogId }}</RouterLink></dd></div></dl></details>
      </div>
    </template>
    <div v-else class="inset raw"><h3>Arguments</h3><pre>{{ pretty(call.argumentsJson) }}</pre><h3>Result</h3><pre>{{ pretty(call.resultJson) }}</pre></div>
  </div>
</template>
<style scoped>
.call-heading { display:flex; align-items:center; justify-content:space-between; gap:20px; padding:10px var(--detail-inset); background:var(--heading-main); color:var(--selection); flex-shrink:0; }
h2 { font-size:22px; margin:0 0 5px; overflow-wrap:anywhere; } .call-heading span,.call-heading time { display:block; font-size:14px; }.call-heading .outcome-symbol { display:inline; }.timing { text-align:right; font-variant-numeric:tabular-nums; }.timing span { margin-top:5px; }
.tabs { display:flex; gap:28px; padding:0 var(--detail-inset); border-bottom:1px solid var(--border); flex-shrink:0; }
.tabs button { border:0; border-bottom:2px solid transparent; background:transparent; border-radius:0; padding:16px 1px 11px; font-size:14px; color:var(--text-muted); }
.tabs button[aria-selected=true] { border-color:var(--selection); color:var(--selection); font-weight:650; }
.detail-scroll { flex:1; overflow:auto; min-height:0; padding:22px 0 34px; scrollbar-width:thin; }
.inset { padding:0 var(--detail-inset); }.request { display:flex; align-items:baseline; flex-wrap:wrap; gap:12px 28px; margin:0 var(--detail-inset); padding:0 0 18px; border-bottom:1px solid var(--border); }.request h3 { margin:0; font-size:16px; }.request dl { display:flex; flex-wrap:wrap; gap:14px 28px; margin:0; }.request dl div { display:flex; align-items:baseline; flex-wrap:wrap; gap:8px 12px; min-width:0; max-width:100%; }.request dd { margin:0; }.request > p { margin:0; } dt { color:var(--text-muted); font-size:14px; } dd { margin:4px 0 0; overflow-wrap:anywhere; }
.technical { border-top:1px solid var(--border); margin-top:20px; }.technical summary { padding-top:14px; font-size:14px; cursor:pointer; color:var(--text-muted); }.technical dl { font-size:14px; }.technical dl > div { margin:14px 0; }.message { white-space:pre-wrap; overflow-wrap:anywhere; }.raw h3 { font-size:16px; }
@media(max-width:720px) { .call-heading { align-items:flex-start; gap:12px; }.timing { max-width:52%; } }
</style>
