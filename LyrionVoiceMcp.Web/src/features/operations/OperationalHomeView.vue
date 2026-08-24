<template>
  <main class="operations-page">
    <section class="hero" aria-labelledby="page-title">
      <div class="hero__mark" aria-hidden="true">
        <span></span>
        <span></span>
        <span></span>
        <span></span>
      </div>
      <h1 id="page-title">Lyrion Voice MCP</h1>
    </section>

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
              :href="`/jobs/${operations.searchIndex.latestJob.id}`"
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
import { useOperationsStore } from './operationsStore';

const operations = useOperationsStore();
const mcpEndpoint = new URL('/mcp', window.location.origin).href;
let operationPollTimer: ReturnType<typeof setTimeout> | undefined;
let operationPollingActive = false;

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

  return 'Not configured';
});

const lmsStatusPillClass = computed(() => ({
  'status-pill--online': operations.lmsConnection?.status === 'online',
  'status-pill--error': operations.lmsConnection?.status === 'unavailable'
}));

const catalogueStatusLabel = computed(() => {
  if (operations.catalogueLoading && operations.catalogue === null) {
    return 'Checking';
  }

  if (operations.catalogueRebuilding) {
    return 'Rebuilding';
  }

  if (operations.catalogue?.latestRefresh?.status === 'failed'
    || operations.catalogue?.latestRefresh?.status === 'interrupted'
    || operations.catalogue?.latestRefresh?.status === 'cancelled') {
    return 'Attention';
  }

  if (operations.catalogue?.summary) {
    return 'Ready';
  }

  return 'Not built';
});

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

const indexStatusLabel = computed(() => {
  if (operations.searchIndexesLoading && !operations.searchIndex) {
    return 'Checking';
  }

  if (operations.searchIndexesRebuilding) {
    return 'Rebuilding';
  }

  if (operations.searchIndex?.latestJob?.status === 'failed') {
    return 'Attention';
  }

  if (operations.searchIndex?.artifact) {
    return 'Ready';
  }

  return 'Not built';
});

const indexStatusPillClass = computed(() => ({
  'status-pill--online': indexStatusLabel.value === 'Ready',
  'status-pill--working': operations.searchIndexesRebuilding,
  'status-pill--error': operations.searchIndexesErrorMessage !== null
    || indexStatusLabel.value === 'Attention'
}));

onMounted(async () => {
  operationPollingActive = true;
  await Promise.all([
    operations.load(),
    operations.loadCatalogue(),
    operations.loadSearchIndexes()
  ]);
  scheduleOperationPoll();
});

onUnmounted(() => {
  operationPollingActive = false;
  clearOperationPoll();
});

async function rebuildCatalogue(): Promise<void> {
  await operations.rebuild();
  scheduleOperationPoll();
}

async function rebuildIndex(): Promise<void> {
  await operations.rebuildIndex();
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
      operations.loadCatalogue(),
      operations.loadSearchIndexes()
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
.operations-page {
  width: min(1120px, calc(100% - 40px));
  margin: 0 auto;
  padding: 60px 0 36px;
}

.hero {
  display: flex;
  align-items: center;
  gap: 28px;
  margin-bottom: 38px;
}

.hero__mark {
  width: 92px;
  height: 92px;
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 1px solid var(--border-strong);
  border-radius: 26px;
  background: linear-gradient(145deg, rgba(244, 175, 65, 0.16), rgba(244, 175, 65, 0.03));
  box-shadow: 0 26px 70px rgba(0, 0, 0, 0.28);
}

.hero__mark span {
  width: 7px;
  border-radius: 99px;
  background: var(--accent);
  box-shadow: 0 0 22px rgba(244, 175, 65, 0.4);
}

.hero__mark span:nth-child(1),
.hero__mark span:nth-child(4) {
  height: 24px;
}

.hero__mark span:nth-child(2) {
  height: 48px;
}

.hero__mark span:nth-child(3) {
  height: 36px;
}

.status-card__label {
  margin: 0 0 8px;
  color: var(--accent);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.13em;
  text-transform: uppercase;
}

h1,
h2,
p {
  margin-top: 0;
}

h1 {
  margin-bottom: 0;
  font-family: var(--font-display);
  font-size: clamp(2.5rem, 6vw, 4.7rem);
  font-weight: 620;
  letter-spacing: -0.055em;
  line-height: 0.98;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.status-card {
  padding: 26px;
  border: 1px solid var(--border);
  border-radius: 20px;
  background: var(--surface);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.16);
}

.status-card__heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.status-card h2 {
  margin-bottom: 18px;
  font-size: 1.22rem;
  font-weight: 650;
}

.status-card__copy,
.error-message {
  color: var(--text-muted);
  line-height: 1.55;
}

.error-message {
  color: var(--danger-text);
}

.status-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 11px;
  border: 1px solid var(--border);
  border-radius: 999px;
  color: var(--text-muted);
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
}

.status-pill__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: currentColor;
}

.status-pill--online {
  border-color: rgba(95, 211, 151, 0.34);
  color: var(--success);
}

.status-pill--error {
  border-color: rgba(255, 119, 119, 0.34);
  color: var(--danger-text);
}

.status-pill--working {
  border-color: rgba(244, 175, 65, 0.4);
  color: var(--accent);
}

.refresh-button {
  padding: 10px 15px;
  border: 1px solid var(--border-strong);
  border-radius: 10px;
  color: var(--text);
  background: transparent;
  font: inherit;
  font-size: 0.85rem;
  cursor: pointer;
}

.refresh-button:hover:not(:disabled) {
  border-color: var(--accent);
  color: var(--accent);
}

.refresh-button:disabled {
  cursor: wait;
  opacity: 0.5;
}

code {
  display: block;
  max-width: 100%;
  margin: 0;
  padding: 9px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  color: var(--accent-soft);
  background: rgba(0, 0, 0, 0.22);
  font-size: 1rem;
  overflow-wrap: anywhere;
}

.connection-url {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.server-version {
  margin: 0;
  color: var(--text-dim);
  font-size: 0.78rem;
}

.status-card--maintenance {
  grid-column: 1 / -1;
  background: linear-gradient(145deg, rgba(38, 34, 27, 0.96), rgba(25, 23, 19, 0.98));
}

.operation-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: 24px;
  padding: 4px 0 22px;
}

.operation-row + .operation-row {
  padding: 22px 0 4px;
  border-top: 1px solid var(--border);
}

.operation-row__title {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 7px;
}

.operation-row h2 {
  margin: 0;
}

.operation-row p {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.83rem;
  line-height: 1.55;
}

.operation-row .error-message + .error-message {
  margin-top: 5px;
}

.operation-row .error-message {
  color: var(--danger-text);
}

.operation-row__actions {
  display: flex;
  align-items: center;
  gap: 14px;
}

.job-link {
  color: var(--accent-soft);
  font-size: 0.78rem;
  white-space: nowrap;
}

.index-rebuild {
  min-width: 92px;
}

.catalogue-rebuild {
  min-width: 170px;
}

footer {
  display: flex;
  gap: 9px;
  justify-content: center;
  padding: 28px 0 0;
  color: var(--text-dim);
  font-size: 0.78rem;
}

@media (max-width: 850px) {
  .operations-page {
    padding-top: 42px;
  }

  .status-grid {
    grid-template-columns: 1fr 1fr;
  }

}

@media (max-width: 560px) {
  .operations-page {
    width: min(100% - 28px, 1120px);
    padding-top: 28px;
  }

  .hero {
    align-items: flex-start;
    gap: 18px;
    margin-bottom: 34px;
  }

  .hero__mark {
    width: 64px;
    height: 64px;
    border-radius: 18px;
  }

  .hero__mark span {
    width: 5px;
  }

  .hero__mark span:nth-child(1),
  .hero__mark span:nth-child(4) {
    height: 16px;
  }

  .hero__mark span:nth-child(2) {
    height: 34px;
  }

  .hero__mark span:nth-child(3) {
    height: 25px;
  }

  .status-grid {
    grid-template-columns: 1fr;
  }

  .operation-row {
    grid-template-columns: 1fr;
    align-items: flex-start;
    gap: 16px;
  }

  .operation-row__actions {
    width: 100%;
    justify-content: space-between;
  }

  footer {
    flex-wrap: wrap;
  }
}
</style>
