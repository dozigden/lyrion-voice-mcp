<template>
  <main class="page">
    <RouterLink class="back" :to="backLink">← {{ backLabel }}</RouterLink>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <div v-else-if="loading || !record" class="loading">Loading record…</div>
    <template v-else>
      <header><p class="eyebrow">{{ eyebrow }}</p><h1>{{ title }}</h1><p>{{ timestamp }}</p></header>
      <section v-if="kind === 'jobs' && job" class="facts">
        <div><strong>{{ job.job.status }}</strong><span>Status</span></div><div><strong>{{ duration(job.job.startedAt, job.job.completedAt) }}</strong><span>Duration</span></div><div><strong>{{ job.logs.length }}</strong><span>Log entries</span></div>
      </section>
      <button v-if="kind === 'jobs' && cancellable" class="action" type="button" @click="requestCancellation">Cancel job</button>
      <section v-if="kind === 'jobs' && job" class="panel">
        <dl><div><dt>Correlation</dt><dd>{{ job.job.correlationId ?? '—' }}</dd></div><div><dt>Run after</dt><dd>{{ formatDate(job.job.runAfter) }}</dd></div><div><dt>Started</dt><dd>{{ formatOptionalDate(job.job.startedAt) }}</dd></div><div><dt>Completed</dt><dd>{{ formatOptionalDate(job.job.completedAt) }}</dd></div></dl>
        <h2>Payload</h2><pre>{{ pretty(job.job.payloadJson) }}</pre><h2>Result</h2><pre>{{ pretty(job.job.resultJson) }}</pre><p v-if="job.job.errorMessage" class="error">{{ job.job.errorMessage }}</p>
      </section>
      <section v-if="kind === 'jobs' && job" class="panel"><h2>Job log</h2><article v-for="entry in job.logs" :key="entry.id" class="log"><time>{{ formatDate(entry.loggedAt) }}</time><strong>{{ entry.level }}</strong><span>{{ entry.message }}</span><pre v-if="entry.dataJson">{{ pretty(entry.dataJson) }}</pre></article></section>
      <section v-if="kind === 'errors' && applicationError" class="panel">
        <dl><div><dt>Source / area</dt><dd>{{ applicationError.source }} / {{ applicationError.area }}</dd></div><div><dt>Trace</dt><dd>{{ applicationError.traceIdentifier ?? '—' }}</dd></div><div><dt>Request</dt><dd>{{ requestLabel }}</dd></div><div><dt>Job</dt><dd><RouterLink v-if="applicationError.jobId" :to="{ name:'jobs-detail',params:{id:applicationError.jobId} }">#{{ applicationError.jobId }}</RouterLink><span v-else>—</span></dd></div><div><dt>Report ID</dt><dd>{{ applicationError.reportId ?? '—' }}</dd></div><div><dt>Stored</dt><dd>{{ formatDate(applicationError.createdAt) }}</dd></div></dl>
        <h2>Message</h2><p>{{ applicationError.message }}</p><h2>Context</h2><pre>{{ pretty(applicationError.contextJson) }}</pre><h2>Stack trace</h2><pre>{{ applicationError.stackTrace ?? 'Not available.' }}</pre>
      </section>
      <section v-if="kind === 'tool-calls' && toolCall" class="panel">
        <dl><div><dt>Status</dt><dd>{{ toolCall.status }}</dd></div><div><dt>Duration</dt><dd>{{ toolCall.durationMilliseconds ?? '—' }} ms</dd></div><div><dt>Completed</dt><dd>{{ formatOptionalDate(toolCall.completedAt) }}</dd></div><div><dt>Trace</dt><dd>{{ toolCall.traceIdentifier ?? '—' }}</dd></div><div><dt>Error log</dt><dd><RouterLink v-if="toolCall.errorLogId" :to="{name:'errors-detail',params:{id:toolCall.errorLogId}}">#{{ toolCall.errorLogId }}</RouterLink><span v-else>—</span></dd></div></dl>
        <p v-if="toolCall.argumentsTruncated || toolCall.resultTruncated" class="warning">One or more JSON values were bounded before storage; the wrapper below records the original size.</p>
        <h2>Arguments</h2><pre>{{ pretty(toolCall.argumentsJson) }}</pre><h2>Result</h2><pre>{{ pretty(toolCall.resultJson) }}</pre><p v-if="toolCall.errorMessage" class="error">{{ toolCall.errorMessage }}</p>
      </section>
    </template>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { cancelJob, getError, getJob, getToolCall, type ErrorLog, type JobDetails, type ToolCall } from './operationalHistoryApi';

const props = defineProps<{ kind: 'jobs' | 'errors' | 'tool-calls' }>(); const route = useRoute();
const job = ref<JobDetails | null>(null); const applicationError = ref<ErrorLog | null>(null); const toolCall = ref<ToolCall | null>(null);
const loading = ref(false); const error = ref<string | null>(null);
const record = computed(() => job.value ?? applicationError.value ?? toolCall.value);
const backLink = computed(() => `/${props.kind}`); const backLabel = computed(() => props.kind === 'jobs' ? 'Jobs' : props.kind === 'errors' ? 'Error log' : 'MCP tool calls');
const eyebrow = computed(() => props.kind === 'jobs' ? `Job #${job.value?.job.id}` : props.kind === 'errors' ? `Error #${applicationError.value?.id}` : `Call ${toolCall.value?.id}`);
const title = computed(() => job.value?.job.type ?? applicationError.value?.exceptionType ?? toolCall.value?.toolName ?? 'Record');
const timestamp = computed(() => { const value = job.value?.job.createdAt ?? applicationError.value?.occurredAt ?? toolCall.value?.startedAt; return value ? formatDate(value) : ''; });
const cancellable = computed(() => job.value?.job.status === 'pending' || job.value?.job.status === 'running');
const requestLabel = computed(() => applicationError.value?.requestMethod && applicationError.value.requestPath ? `${applicationError.value.requestMethod} ${applicationError.value.requestPath}` : '—');

watch(() => [props.kind, route.params.id], load); onMounted(load);
async function load(): Promise<void> { loading.value=true; error.value=null; job.value=null; applicationError.value=null; toolCall.value=null; try { const id=String(route.params.id); if(props.kind==='jobs') job.value=await getJob(id); else if(props.kind==='errors') applicationError.value=await getError(id); else toolCall.value=await getToolCall(id); } catch(reason){error.value=reason instanceof Error?reason.message:'The record could not be loaded.';} finally{loading.value=false;} }
async function requestCancellation(): Promise<void> { if(!job.value)return; try{await cancelJob(job.value.job.id);await load();}catch(reason){error.value=reason instanceof Error?reason.message:'Cancellation failed.';} }
function pretty(value:string|null):string { if(!value)return 'Not available.'; try{return JSON.stringify(JSON.parse(value),null,2);}catch{return value;} }
function formatDate(value:string):string{return new Intl.DateTimeFormat(undefined,{dateStyle:'full',timeStyle:'medium'}).format(new Date(value));}
function formatOptionalDate(value:string|null):string{return value ? formatDate(value) : '—';}
function duration(start:string|null,end:string|null):string{if(!start)return 'Not started';if(!end)return 'Running';return `${Math.max(0,new Date(end).getTime()-new Date(start).getTime())} ms`;}
</script>

<style scoped>
.page{width:min(1050px,calc(100% - 40px));margin:0 auto;padding:42px 0 64px}.back{display:inline-block;margin-bottom:32px;color:var(--text-muted);text-decoration:none}.back:hover,a{color:var(--accent)}header{margin-bottom:24px}.eyebrow{color:var(--accent);font-size:.75rem;font-weight:700;letter-spacing:.1em;text-transform:uppercase}h1{margin:0 0 8px;font:620 clamp(2rem,5vw,3.8rem)/1 var(--font-display)}header p:last-child{color:var(--text-muted)}.facts{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:16px}.facts div,.panel{border:1px solid var(--border);border-radius:14px;background:var(--surface)}.facts div{padding:17px}.facts strong,.facts span{display:block}.facts span,dt{margin-top:5px;color:var(--text-muted);font-size:.78rem}.panel{margin:16px 0;padding:22px}h2{margin:22px 0 10px;font-size:1rem}.panel h2:first-child{margin-top:0}pre{overflow:auto;max-height:520px;margin:0;padding:14px;border-radius:9px;color:var(--accent-soft);background:rgba(0,0,0,.28);font:12px/1.55 ui-monospace,monospace;white-space:pre-wrap;word-break:break-word}.log{display:grid;grid-template-columns:180px 90px 1fr;gap:12px;padding:12px 0;border-top:1px solid var(--border);font-size:.82rem}.log time{color:var(--text-dim)}.log pre{grid-column:1/-1}.panel dl{display:grid;grid-template-columns:repeat(2,1fr);gap:12px}.panel dl div{padding:12px;border:1px solid var(--border);border-radius:9px}dt{margin:0 0 5px}dd{margin:0;word-break:break-all}.action{padding:10px 14px;border:1px solid var(--danger-text);border-radius:9px;color:var(--danger-text);background:transparent;cursor:pointer}.error{color:var(--danger-text)}.warning{color:var(--accent-soft)}.loading{color:var(--text-muted)}@media(max-width:700px){.facts,.panel dl{grid-template-columns:1fr}.log{grid-template-columns:1fr}}
</style>
