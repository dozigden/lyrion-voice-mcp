<template>
  <main class="operations-page">
    <header class="page-heading"><h1 id="page-title">System overview</h1></header>

    <section class="status-grid" aria-label="Service status">
      <article class="status-card">
        <div class="status-card__heading">
          <div>
            <p class="status-card__label">LMS connection</p>
            <h2>{{ operations.lmsConnection?.serverId ?? 'Not configured' }}</h2>
          </div>
          <span
            class="status-pill"
            :class="lmsStatusPillClass"
            role="status"
          >
            <span class="status-pill__dot" aria-hidden="true"></span>
            {{ lmsStatusLabel }}
          </span>
        </div>
        <code v-if="operations.lmsConnection?.baseUrl" class="connection-url">
          {{ operations.lmsConnection.baseUrl }}
        </code>
        <p class="status-card__copy">
          {{ operations.lmsConnection?.message ?? 'LMS connection status is unavailable.' }}
        </p>
        <p v-if="operations.errorMessage" class="error-message" role="alert">
          {{ operations.errorMessage }}
        </p>
        <p v-if="operations.lmsConnection?.serverVersion" class="server-version">
          LMS {{ operations.lmsConnection.serverVersion }}
        </p>
      </article>

      <article class="status-card status-card--endpoint" aria-labelledby="mcp-endpoint-title">
        <p id="mcp-endpoint-title" class="status-card__label">MCP endpoint</p>
        <code>{{ mcpEndpoint }}</code>
      </article>

      <article class="status-card status-card--maintenance" aria-label="Catalogue maintenance">
        <section class="operation-row">
          <div class="operation-row__summary">
            <div class="operation-row__title">
              <h2>Catalogue sync</h2>
              <span class="status-pill" :class="catalogueStatusPillClass" role="status">
                <span class="status-pill__dot" aria-hidden="true"></span>
                {{ catalogueStatusLabel }}
              </span>
            </div>
            <p v-if="operations.catalogueErrorMessage" class="error-message" role="alert">
              {{ operations.catalogueErrorMessage }}
            </p>
            <p v-else-if="operations.catalogue?.summary">
              {{ formatCount(operations.catalogue.summary.trackCount) }} tracks ·
              <time :datetime="operations.catalogue.summary.refreshedAt">
                {{ formatDate(operations.catalogue.summary.refreshedAt) }}
              </time>
            </p>
            <p v-else-if="!operations.catalogueLoading">Not built.</p>
            <p v-if="operations.catalogue?.latestRefresh?.failureMessage" class="error-message">
              {{ operations.catalogue.latestRefresh.failureMessage }}
            </p>
          </div>
          <button
            class="refresh-button catalogue-rebuild"
            type="button"
            :disabled="catalogueButtonDisabled"
            @click="rebuildCatalogue"
          >
            Rebuild
          </button>
        </section>

        <section class="operation-row">
          <div class="operation-row__summary">
            <div class="operation-row__title">
              <h2>Search index</h2>
              <span class="status-pill" :class="indexStatusPillClass" role="status">
                <span class="status-pill__dot" aria-hidden="true"></span>
                {{ indexStatusLabel }}
              </span>
            </div>
            <p v-if="operations.searchIndexesErrorMessage" class="error-message" role="alert">
              {{ operations.searchIndexesErrorMessage }}
            </p>
            <p v-else-if="operations.searchIndexesLoading && !operations.searchIndex">
              Checking…
            </p>
            <p v-else-if="operations.searchIndex?.artifact">
              {{ operations.searchIndex.resolver }} ·
              {{ formatCount(operations.searchIndex.artifact.candidateCount) }} candidates ·
              {{ formatBytes(operations.searchIndex.artifact.indexSizeBytes) }} ·
              <time :datetime="operations.searchIndex.artifact.builtAt">
                {{ formatDate(operations.searchIndex.artifact.builtAt) }}
              </time>
            </p>
            <p v-else>Not built.</p>
            <p v-if="operations.searchIndex?.latestJob?.errorMessage" class="error-message">
              {{ operations.searchIndex.latestJob.errorMessage }}
            </p>
          </div>
          <div class="operation-row__actions">
            <a
              v-if="operations.searchIndex?.latestJob"
              class="job-link"
              :href="`/system/jobs/${operations.searchIndex.latestJob.id}`"
            >
              Job {{ operations.searchIndex.latestJob.id }} · {{ operations.searchIndex.latestJob.status }}
            </a>
            <button
              class="refresh-button index-rebuild"
              type="button"
              :disabled="indexButtonDisabled(operations.searchIndex?.latestJob?.status)"
              @click="rebuildIndex()"
            >
              Rebuild
            </button>
          </div>
        </section>
      </article>
    </section>

    <footer>
      <span>Trusted LAN only</span>
      <span aria-hidden="true">·</span>
      <span>{{ operations.version?.version ?? 'Version unavailable' }}</span>
    </footer>
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
  'status-pill--online': operations.lmsConnection?.status === 'online',
  'status-pill--error': operations.lmsConnection?.status === 'unavailable'
}));

const catalogueStatusLabel = computed(() => catalogueHeadline(operations.catalogue, operations.catalogueLoading, operations.catalogueErrorMessage));

const catalogueStatusPillClass = computed(() => ({
  'status-pill--online': operations.catalogue !== null
    && operations.catalogue.summary !== null
    && !operations.catalogueRebuilding,
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
.operations-page { width:min(1200px,100%); margin:0 auto; padding:28px 34px 40px; }
.page-heading h1 { margin:0 0 24px; font-size:22px; }
.status-grid { display:grid; grid-template-columns:1fr 1fr; gap:24px 32px; }
.status-card { min-width:0; }.status-card__heading { display:flex; justify-content:space-between; align-items:start; gap:20px; }
.status-card__label { margin:0 0 8px; font-size:14px; color:var(--text-muted); }.status-card h2 { font-size:18px; margin:0 0 8px; }
.status-card__copy,.server-version { color:var(--text-muted); font-size:14px; }
.status-card--endpoint { padding:18px 24px; background:var(--heading-band); align-self:start; color:var(--selection); }.status-card--endpoint .status-card__label { color:inherit; }
.status-card--maintenance { grid-column:1/-1; }
.operation-row { padding:0 0 22px; margin:8px 0 24px; border-bottom:1px solid var(--border); display:flex; justify-content:space-between; align-items:center; gap:24px; }
.operation-row__summary { flex:1; min-width:0; }.operation-row__title { padding:10px 18px; background:var(--heading-band); color:var(--selection); display:flex; justify-content:space-between; align-items:center; gap:16px; }.operation-row__title h2 { margin:0; font-size:16px; }.operation-row__summary > p { font-size:14px; margin:14px 18px 0; }
.operation-row__actions { display:flex; flex-direction:column; align-items:flex-end; gap:12px; }.job-link { font-size:14px; }.status-pill { font-size:14px; white-space:nowrap; }.status-pill--error { color:var(--danger-text); }
.refresh-button { color:var(--selection); background:transparent; border-color:var(--selection); }.error-message { color:var(--danger-text); }
footer { display:flex; gap:10px; color:var(--text-muted); font-size:14px; border-top:1px solid var(--border); padding-top:16px; }
@media(max-width:720px) { .operations-page { padding:24px 18px; }.status-grid { grid-template-columns:1fr; }.operation-row { align-items:stretch; flex-direction:column; gap:16px; }.operation-row > button { align-self:flex-start; }.operation-row__actions { flex-direction:row; justify-content:space-between; align-items:center; } }
</style>
