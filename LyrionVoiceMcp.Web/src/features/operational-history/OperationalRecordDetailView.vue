<template>
  <main class="page">
    <RouterLink class="back" :to="backLink">← {{ backLabel }}</RouterLink>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <div v-if="loading" class="loading">Loading record…</div>
    <template v-else-if="record">
      <header><p class="eyebrow">{{ eyebrow }}</p><h1>{{ title }}</h1><p>{{ timestamp }}</p></header>
      <section v-if="kind === 'jobs' && job" class="facts">
        <div><strong>{{ job.job.status }}</strong><span>Status</span></div><div><strong>{{ duration(job.job.startedAt, job.job.completedAt) }}</strong><span>Duration</span></div><div><strong>{{ job.logs.length }}</strong><span>Log entries</span></div>
      </section>
      <button v-if="kind === 'jobs' && cancellable" class="action" type="button" :disabled="history.mutationPending" @click="requestCancellation">Cancel job</button>
      <section v-if="kind === 'jobs' && job" class="panel">
        <dl><div><dt>Correlation</dt><dd>{{ job.job.correlationId ?? '—' }}</dd></div><div><dt>Run after</dt><dd>{{ formatDate(job.job.runAfter) }}</dd></div><div><dt>Started</dt><dd>{{ formatOptionalDate(job.job.startedAt) }}</dd></div><div><dt>Completed</dt><dd>{{ formatOptionalDate(job.job.completedAt) }}</dd></div></dl>
        <h2>Payload</h2><pre>{{ pretty(job.job.payloadJson) }}</pre><h2>Result</h2><pre>{{ pretty(job.job.resultJson) }}</pre><p v-if="job.job.errorMessage" class="error">{{ job.job.errorMessage }}</p>
      </section>
      <section v-if="kind === 'jobs' && job" class="panel"><h2>Job log</h2><article v-for="entry in job.logs" :key="entry.id" class="log"><time>{{ formatDate(entry.loggedAt) }}</time><strong>{{ entry.level }}</strong><span>{{ entry.message }}</span><pre v-if="entry.dataJson">{{ pretty(entry.dataJson) }}</pre></article></section>
      <section v-if="kind === 'errors' && applicationError" class="panel">
        <dl><div><dt>Source / area</dt><dd>{{ applicationError.source }} / {{ applicationError.area }}</dd></div><div><dt>Trace</dt><dd>{{ applicationError.traceIdentifier ?? '—' }}</dd></div><div><dt>Request</dt><dd>{{ requestLabel }}</dd></div><div><dt>Job</dt><dd><RouterLink v-if="applicationError.jobId" :to="{ name:'jobs-detail',params:{id:applicationError.jobId} }">#{{ applicationError.jobId }}</RouterLink><span v-else>—</span></dd></div><div><dt>Report ID</dt><dd>{{ applicationError.reportId ?? '—' }}</dd></div><div><dt>Stored</dt><dd>{{ formatDate(applicationError.createdAt) }}</dd></div></dl>
        <h2>Message</h2><p>{{ applicationError.message }}</p><h2>Context</h2><pre>{{ pretty(applicationError.contextJson) }}</pre><h2>Stack trace</h2><pre>{{ applicationError.stackTrace ?? 'Not available.' }}</pre>
      </section>
    </template>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { storeToRefs } from 'pinia';
import { useHistoryStore, type HistoryKind } from './historyStore';
import { pretty, formatDate } from '../../shared/format';
const props = defineProps<{ kind: HistoryKind }>(), route = useRoute();
const history = useHistoryStore();
const { record, detailLoading: loading } = storeToRefs(history);
const error = computed(() => history.detailError ?? history.mutationError);
const job = computed(() => record.value && 'job' in record.value ? record.value : null);
const applicationError = computed(() => record.value && 'exceptionType' in record.value ? record.value : null);
const backLink = computed(() => ({ name: props.kind }));
const backLabel = computed(() => props.kind === 'jobs' ? 'Jobs' : 'Error log');
const eyebrow = computed(() => props.kind === 'jobs' ? `Job #${job.value?.job.id}` : `Error #${applicationError.value?.id}`);
const title = computed(() => job.value?.job.type ?? applicationError.value?.exceptionType ?? 'Record');
const timestamp = computed(() => formatDate(job.value?.job.createdAt ?? applicationError.value?.occurredAt));
const cancellable = computed(() => ['pending', 'running'].includes(job.value?.job.status ?? ''));
const requestLabel = computed(() => applicationError.value?.requestMethod && applicationError.value.requestPath ? `${applicationError.value.requestMethod} ${applicationError.value.requestPath}` : '—');
watch(() => [props.kind, route.params.id], () => { void history.loadDetail(props.kind, String(route.params.id)); }, { immediate: true });
onBeforeUnmount(history.cancelDetail);
async function requestCancellation() { if (job.value) await history.cancelJob(job.value.job.id); }
const formatOptionalDate = formatDate;
function duration(start: string | null, end: string | null) {
  if (!start) return 'Not started';
  if (!end) return 'Not completed';
  return `${Math.max(0, new Date(end).getTime() - new Date(start).getTime())} ms`;
}
</script>
<style scoped>
.page { width:min(1200px,100%); margin:0 auto; padding:28px 34px 40px; }.back { display:inline-block; margin-bottom:20px; font-size:14px; }header { padding:14px 22px; background:var(--heading-main); color:var(--selection); }h1 { margin:4px 0; font-size:22px; overflow-wrap:anywhere; }.eyebrow,header p:last-child { font-size:14px; margin:0; }
.facts { display:flex; gap:36px; margin:24px 0; }.facts strong,.facts span { display:block; }.facts span,dt { font-size:14px; color:var(--text-muted); }.panel { margin:24px 0; }h2 { padding:10px 22px; background:var(--heading-band); color:var(--selection); font-size:16px; margin:24px 0 16px; }.panel dl { display:grid; grid-template-columns:1fr 1fr; gap:18px 30px; }dt { margin-bottom:4px; }dd { margin:0; overflow-wrap:anywhere; }
.log { display:grid; grid-template-columns:200px 90px minmax(0,1fr); gap:12px; padding:14px 0; border-bottom:1px solid var(--border); font-size:14px; }.log pre { grid-column:1/-1; }.log time { color:var(--text-muted); }.action { color:var(--danger-text); border-color:var(--danger-text); background:transparent; }
@media(max-width:720px) { .page { padding:24px 18px; }.panel dl,.log { grid-template-columns:1fr; }.facts { flex-wrap:wrap; gap:18px 30px; } }
</style>
