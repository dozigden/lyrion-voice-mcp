<template>
  <main class="operations-page">
    <header class="page-heading"><h1 id="page-title">System overview</h1></header>

    <section class="connections" aria-label="Connections">
      <article class="connection" aria-labelledby="lms-title">
        <div class="connection-heading">
          <h2 id="lms-title">LMS connection</h2>
          <span class="status-pill" :class="lmsStatusPillClass" role="status">
            <span class="status-pill__dot" aria-hidden="true"></span>{{ lmsStatusLabel }}
          </span>
        </div>
        <p class="connection-identity">
          <strong>{{ operations.lmsConnection?.serverId ?? 'Not configured' }}</strong>
          <span v-if="operations.lmsConnection?.serverVersion" class="muted">LMS {{ operations.lmsConnection.serverVersion }}</span>
        </p>
        <code v-if="operations.lmsConnection?.baseUrl">{{ operations.lmsConnection.baseUrl }}</code>
        <p v-if="operations.errorMessage" class="error-message" role="alert">{{ operations.errorMessage }}</p>
        <p v-else-if="operations.lmsConnection?.status !== 'online'" class="muted">
          {{ operations.lmsConnection?.message ?? 'LMS connection status is unavailable.' }}
        </p>
      </article>
      <article class="connection" aria-labelledby="mcp-endpoint-title">
        <div class="connection-heading"><h2 id="mcp-endpoint-title">MCP endpoint</h2></div>
        <code>{{ mcpEndpoint }}</code>
      </article>
    </section>

    <section class="maintenance" aria-label="Catalogue maintenance">
      <section class="operation-row" aria-labelledby="catalogue-title">
        <header class="operation-row__title">
          <div class="operation-heading">
            <h2 id="catalogue-title">Catalogue sync</h2>
            <span class="status-pill" :class="catalogueStatusPillClass" role="status">
              <span class="status-pill__dot" aria-hidden="true"></span>{{ catalogueStatusLabel }}
            </span>
          </div>
          <button class="refresh-button catalogue-rebuild" type="button" aria-label="Rebuild catalogue"
            :disabled="catalogueButtonDisabled" @click="rebuildCatalogue">Rebuild</button>
        </header>
        <div class="operation-row__summary">
          <p v-if="operations.catalogueErrorMessage" class="error-message" role="alert">{{ operations.catalogueErrorMessage }}</p>
          <p v-else-if="operations.catalogue?.summary" class="operation-facts">
            <span>{{ formatCount(operations.catalogue.summary.trackCount) }} tracks</span>
            <span>Updated <time :datetime="operations.catalogue.summary.refreshedAt">{{ formatDate(operations.catalogue.summary.refreshedAt) }}</time></span>
          </p>
          <p v-else-if="operations.catalogueLoading" class="muted">Checking…</p>
          <p v-else class="muted">Not built.</p>
          <p v-if="operations.catalogue?.latestRefresh?.failureMessage" class="error-message">{{ operations.catalogue.latestRefresh.failureMessage }}</p>
        </div>
      </section>

      <section class="operation-row" aria-labelledby="index-title">
        <header class="operation-row__title">
          <div class="operation-heading">
            <h2 id="index-title">Search index</h2>
            <span class="status-pill" :class="indexStatusPillClass" role="status">
              <span class="status-pill__dot" aria-hidden="true"></span>{{ indexStatusLabel }}
            </span>
          </div>
          <button class="refresh-button index-rebuild" type="button" aria-label="Rebuild search index"
            :disabled="indexButtonDisabled(operations.searchIndex?.latestJob?.status)" @click="rebuildIndex">Rebuild</button>
        </header>
        <div class="operation-row__summary">
          <p v-if="operations.searchIndexesErrorMessage" class="error-message" role="alert">{{ operations.searchIndexesErrorMessage }}</p>
          <p v-else-if="operations.searchIndexesLoading && !operations.searchIndex" class="muted">Checking…</p>
          <p v-else-if="operations.searchIndex?.artifact" class="operation-facts">
            <span>{{ operations.searchIndex.resolver }}</span>
            <span>{{ formatCount(operations.searchIndex.artifact.candidateCount) }} candidates</span>
            <span>{{ formatBytes(operations.searchIndex.artifact.indexSizeBytes) }}</span>
            <span>Built <time :datetime="operations.searchIndex.artifact.builtAt">{{ formatDate(operations.searchIndex.artifact.builtAt) }}</time></span>
          </p>
          <p v-else class="muted">Not built.</p>
          <p v-if="operations.searchIndex?.latestJob">
            <a class="job-link" :href="`/system/jobs/${operations.searchIndex.latestJob.id}`">Job {{ operations.searchIndex.latestJob.id }} · {{ operations.searchIndex.latestJob.status }}</a>
          </p>
          <p v-if="operations.searchIndex?.latestJob?.errorMessage" class="error-message">{{ operations.searchIndex.latestJob.errorMessage }}</p>
        </div>
      </section>
    </section>

    <footer>{{ operations.version?.version ?? 'Version unavailable' }}</footer>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue';
import { catalogueHeadline, indexHeadline } from './headlineStatus';
import { useOperationsStore } from './operationsStore';

const operations = useOperationsStore();
const mcpEndpoint = new URL('/mcp', window.location.origin).href;
let operationPollTimer: ReturnType<typeof setTimeout> | undefined;
let operationPollingActive = false;
const controller = new AbortController();

const lmsStatusLabel = computed(() => {
  if (operations.loading) {
    return 'Checking';
  }

  if (operations.lmsConnection?.status === 'online') {
    return 'Online';
  }

  if (operations.lmsConnection?.status === 'unavailable') {
    return 'Unavailable';
  }

  if (operations.errorMessage) return 'Unavailable';
  return 'Not configured';
});

const lmsStatusPillClass = computed(() => ({
  'status-pill--online': lmsStatusLabel.value === 'Online',
  'status-pill--error': lmsStatusLabel.value === 'Unavailable'
}));

const catalogueStatusLabel = computed(() => catalogueHeadline(operations.catalogue, operations.catalogueLoading, operations.catalogueErrorMessage));

const catalogueStatusPillClass = computed(() => ({
  'status-pill--online': catalogueStatusLabel.value === 'Ready',
  'status-pill--attention': catalogueStatusLabel.value === 'Ready · attention',
  'status-pill--working': operations.catalogueRebuilding,
  'status-pill--error': operations.catalogueErrorMessage !== null
    || catalogueStatusLabel.value === 'Attention'
}));

const catalogueButtonDisabled = computed(() =>
  operations.catalogueLoading
  || operations.catalogueRebuildPending
  || operations.catalogueRebuilding);

const indexStatusLabel = computed(() => indexHeadline(operations.searchIndex, operations.searchIndexesLoading, operations.searchIndexesErrorMessage));

const indexStatusPillClass = computed(() => ({
  'status-pill--online': indexStatusLabel.value === 'Ready',
  'status-pill--attention': indexStatusLabel.value === 'Ready · attention',
  'status-pill--working': operations.searchIndexesRebuilding,
  'status-pill--error': operations.searchIndexesErrorMessage !== null
    || indexStatusLabel.value === 'Attention'
}));

onMounted(async () => {
  operationPollingActive = true;
  await Promise.all([
    operations.load(controller.signal),
    operations.loadCatalogue(controller.signal),
    operations.loadSearchIndexes(controller.signal)
  ]);
  scheduleOperationPoll();
});

onUnmounted(() => {
  operationPollingActive = false;
  controller.abort();
  clearOperationPoll();
});

async function rebuildCatalogue(): Promise<void> {
  await operations.rebuild(controller.signal);
  scheduleOperationPoll();
}

async function rebuildIndex(): Promise<void> {
  await operations.rebuildIndex(controller.signal);
  scheduleOperationPoll();
}

function scheduleOperationPoll(): void {
  clearOperationPoll();
  if (!operationPollingActive
    || (!operations.catalogueRebuilding && !operations.searchIndexesRebuilding)) {
    return;
  }

  operationPollTimer = setTimeout(async () => {
    await Promise.all([
      operations.loadCatalogue(controller.signal),
      operations.loadSearchIndexes(controller.signal)
    ]);
    scheduleOperationPoll();
  }, 2_000);
}

function clearOperationPoll(): void {
  if (operationPollTimer !== undefined) {
    clearTimeout(operationPollTimer);
    operationPollTimer = undefined;
  }
}

function indexButtonDisabled(status: string | undefined): boolean {
  return operations.searchIndexesLoading
    || operations.searchIndexRebuildPending
    || status === 'pending'
    || status === 'running'
    || operations.catalogueRebuilding
    || !operations.catalogue?.summary;
}

function formatCount(value: number): string {
  return value.toLocaleString('en-GB');
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short'
  });
}

function formatBytes(value: number): string {
  if (value < 1_024) {
    return `${value} B`;
  }

  if (value < 1_048_576) {
    return `${(value / 1_024).toFixed(1)} KiB`;
  }

  return `${(value / 1_048_576).toFixed(1)} MiB`;
}
</script>

<style scoped>
.operations-page { width:min(1200px,100%); margin:0 auto; padding:22px 34px 32px; }
.page-heading h1 { margin:0 0 20px; font-size:22px; }
.connections { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:16px 32px; margin-bottom:24px; }
.connection { min-width:0; }
.connection-heading { display:flex; align-items:center; flex-wrap:wrap; gap:8px 16px; margin-bottom:8px; }
.connection-heading h2 { margin:0; font-size:16px; }
.connection p { margin:8px 0 0; font-size:14px; }
.connection .connection-identity { display:flex; flex-wrap:wrap; align-items:baseline; gap:4px 16px; margin:0 0 4px; }
.connection-identity strong { font-size:16px; overflow-wrap:anywhere; }
.connection code { display:block; overflow-wrap:anywhere; font-size:14px; }
.maintenance { display:grid; gap:20px; }
.operation-row { min-width:0; }
.operation-row__title { display:grid; grid-template-columns:minmax(0,1fr) auto; align-items:center; gap:16px; padding:8px 16px; background:var(--heading-band); color:var(--selection); }
.operation-heading { display:flex; align-items:center; flex-wrap:wrap; gap:4px 16px; min-width:0; }
.operation-heading h2 { margin:0; font-size:16px; }
.operation-row__summary { padding:0 16px; overflow-wrap:anywhere; }
.operation-row__summary p { margin:8px 0 0; font-size:14px; }
.operation-facts { display:flex; flex-wrap:wrap; gap:4px 18px; }
.status-pill { display:inline-flex; align-items:center; gap:6px; font-size:14px; color:var(--text-muted); }
.status-pill__dot { width:6px; height:6px; border-radius:50%; background:currentColor; flex:none; }
.status-pill--online { color:var(--success); }
.status-pill--working,.status-pill--attention { color:#805500; }
.status-pill--error,.error-message { color:var(--danger-text); }
.refresh-button { color:var(--selection); background:transparent; border-color:var(--selection); padding:6px 12px; font-size:14px; }
footer { margin-top:20px; padding-top:12px; border-top:1px solid var(--border); color:var(--text-muted); font-size:14px; }
@media(max-width:720px) {
  .operations-page { padding:18px 18px 24px; }
  .page-heading h1 { margin-bottom:16px; }
  .connections { grid-template-columns:minmax(0,1fr); gap:16px; margin-bottom:20px; }
  .operation-row__title { padding:8px 12px; gap:12px; }
  .operation-heading { flex-direction:column; align-items:flex-start; }
  .operation-row__summary { padding:0 12px; }
}
</style>
