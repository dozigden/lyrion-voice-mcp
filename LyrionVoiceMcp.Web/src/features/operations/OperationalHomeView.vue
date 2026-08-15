<template>
  <main class="operations-page">
    <section class="hero" aria-labelledby="page-title">
      <div class="hero__mark" aria-hidden="true">
        <span></span>
        <span></span>
        <span></span>
        <span></span>
      </div>
      <div>
        <p class="eyebrow">Lyrion Music Server · Model Context Protocol</p>
        <h1 id="page-title">Lyrion Voice MCP</h1>
        <p class="hero__summary">
          A voice-oriented bridge to your music library and players.
        </p>
      </div>
    </section>

    <section class="status-grid" aria-label="Service status">
      <article class="status-card status-card--primary">
        <div class="status-card__heading">
          <div>
            <p class="status-card__label">Service</p>
            <h2>Runtime</h2>
          </div>
          <span
            class="status-pill"
            :class="statusPillClass"
            role="status"
          >
            <span class="status-pill__dot" aria-hidden="true"></span>
            {{ statusLabel }}
          </span>
        </div>

        <p v-if="operations.errorMessage" class="error-message">
          {{ operations.errorMessage }}
        </p>
        <p v-else class="status-card__copy">
          The HTTP service is responding and ready for MCP clients on this network.
        </p>

        <button
          class="refresh-button"
          type="button"
          :disabled="operations.loading"
          @click="refresh"
        >
          Refresh status
        </button>
      </article>

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
        <p v-if="operations.lmsConnection?.serverVersion" class="server-version">
          LMS {{ operations.lmsConnection.serverVersion }}
        </p>
      </article>

      <article class="status-card">
        <p class="status-card__label">MCP endpoint</p>
        <h2>Streamable HTTP</h2>
        <code>/mcp</code>
        <p class="status-card__copy">Stateless transport using the official C# SDK.</p>
      </article>

      <article class="status-card">
        <p class="status-card__label">Build</p>
        <h2>{{ operations.version?.version ?? 'Unavailable' }}</h2>
        <dl class="build-details">
          <div>
            <dt>Channel</dt>
            <dd>{{ operations.version?.channel ?? '—' }}</dd>
          </div>
          <div>
            <dt>Build</dt>
            <dd>{{ operations.version?.build ?? '—' }}</dd>
          </div>
          <div>
            <dt>Commit</dt>
            <dd>{{ operations.version?.commit ?? '—' }}</dd>
          </div>
        </dl>
      </article>

      <article class="status-card status-card--catalogue">
        <div class="status-card__heading">
          <div>
            <p class="status-card__label">Canonical catalogue</p>
            <h2>Library snapshot</h2>
          </div>
          <span class="status-pill" :class="catalogueStatusPillClass" role="status">
            <span class="status-pill__dot" aria-hidden="true"></span>
            {{ catalogueStatusLabel }}
          </span>
        </div>

        <p v-if="operations.catalogueErrorMessage" class="error-message" role="alert">
          {{ operations.catalogueErrorMessage }}
        </p>
        <p v-else-if="operations.catalogue?.summary" class="status-card__copy">
          {{ formatCount(operations.catalogue.summary.trackCount) }} tracks. Last rebuilt
          <time :datetime="operations.catalogue.summary.refreshedAt">
            {{ formatDate(operations.catalogue.summary.refreshedAt) }}.
          </time>
        </p>
        <p v-else-if="!operations.catalogueLoading" class="catalogue-empty">
          The catalogue has not been built yet. Rebuilding reads LMS metadata without altering media or playback.
        </p>
        <p v-if="operations.catalogue?.latestRefresh?.failureMessage" class="error-message">
          {{ operations.catalogue.latestRefresh.failureMessage }}
        </p>

        <button
          class="refresh-button catalogue-rebuild"
          type="button"
          :disabled="catalogueButtonDisabled"
          @click="rebuildCatalogue"
        >
          {{ catalogueButtonLabel }}
        </button>
      </article>
    </section>

    <aside class="trust-notice" aria-labelledby="trust-title">
      <span class="trust-notice__icon" aria-hidden="true">!</span>
      <div>
        <h2 id="trust-title">Trusted network only</h2>
        <p>
          This service has no authentication and can control discovered players. Keep it on a trusted LAN and do not expose it to the public internet.
        </p>
      </div>
    </aside>

    <footer>
      <span>Trusted-LAN service</span>
      <span aria-hidden="true">·</span>
      <span>Search, player status and playback available</span>
    </footer>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue';
import { useOperationsStore } from './operationsStore';

const operations = useOperationsStore();
let cataloguePollTimer: ReturnType<typeof setTimeout> | undefined;
let cataloguePollingActive = false;

const statusLabel = computed(() => {
  if (operations.loading) {
    return 'Checking';
  }

  if (operations.isHealthy) {
    return 'Online';
  }

  return 'Unavailable';
});

const statusPillClass = computed(() => ({
  'status-pill--online': operations.isHealthy,
  'status-pill--error': !operations.loading && !operations.isHealthy
}));

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

const catalogueButtonLabel = computed(() => {
  if (operations.catalogueRebuildPending) {
    return 'Starting rebuild…';
  }

  if (operations.catalogueRebuilding) {
    return 'Rebuild in progress…';
  }

  return 'Rebuild catalogue';
});

onMounted(async () => {
  cataloguePollingActive = true;
  await Promise.all([
    operations.load(),
    operations.loadCatalogue()
  ]);
  scheduleCataloguePoll();
});

onUnmounted(() => {
  cataloguePollingActive = false;
  clearCataloguePoll();
});

async function refresh(): Promise<void> {
  await operations.load();
}

async function rebuildCatalogue(): Promise<void> {
  await operations.rebuild();
  scheduleCataloguePoll();
}

function scheduleCataloguePoll(): void {
  clearCataloguePoll();
  if (!cataloguePollingActive || !operations.catalogueRebuilding) {
    return;
  }

  cataloguePollTimer = setTimeout(async () => {
    await operations.loadCatalogue();
    scheduleCataloguePoll();
  }, 2_000);
}

function clearCataloguePoll(): void {
  if (cataloguePollTimer !== undefined) {
    clearTimeout(cataloguePollTimer);
    cataloguePollTimer = undefined;
  }
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
</script>

<style scoped>
.operations-page {
  width: min(1120px, calc(100% - 40px));
  margin: 0 auto;
  padding: 72px 0 36px;
}

.hero {
  display: flex;
  align-items: center;
  gap: 28px;
  margin-bottom: 52px;
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

.eyebrow,
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
  margin-bottom: 10px;
  font-family: var(--font-display);
  font-size: clamp(2.5rem, 6vw, 4.7rem);
  font-weight: 620;
  letter-spacing: -0.055em;
  line-height: 0.98;
}

.hero__summary {
  margin-bottom: 0;
  color: var(--text-muted);
  font-size: 1.08rem;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.status-card {
  min-height: 235px;
  padding: 26px;
  border: 1px solid var(--border);
  border-radius: 20px;
  background: var(--surface);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.16);
}

.status-card--primary {
  background: linear-gradient(145deg, rgba(38, 34, 27, 0.98), rgba(25, 23, 19, 0.98));
}

.status-card__heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.status-card h2,
.trust-notice h2 {
  margin-bottom: 18px;
  font-size: 1.22rem;
  font-weight: 650;
}

.status-card__copy,
.error-message {
  min-height: 48px;
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
  display: inline-block;
  margin: 0 0 18px;
  padding: 9px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  color: var(--accent-soft);
  background: rgba(0, 0, 0, 0.22);
  font-size: 1rem;
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

.build-details {
  display: grid;
  gap: 8px;
  margin: 0;
}

.build-details div {
  display: grid;
  grid-template-columns: 70px minmax(0, 1fr);
  gap: 10px;
}

.build-details dt {
  color: var(--text-dim);
}

.build-details dd {
  min-width: 0;
  margin: 0;
  overflow: hidden;
  color: var(--text-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.status-card--catalogue {
  min-height: 190px;
  grid-column: 1 / -1;
  background: linear-gradient(145deg, rgba(38, 34, 27, 0.96), rgba(25, 23, 19, 0.98));
}

.catalogue-empty {
  min-height: 48px;
  color: var(--text-muted);
  line-height: 1.55;
}

.catalogue-rebuild {
  min-width: 170px;
}

.trust-notice {
  display: flex;
  gap: 18px;
  margin-top: 18px;
  padding: 22px 24px;
  border: 1px solid rgba(244, 175, 65, 0.25);
  border-radius: 16px;
  background: rgba(244, 175, 65, 0.055);
}

.trust-notice__icon {
  width: 28px;
  height: 28px;
  flex: 0 0 auto;
  display: grid;
  place-items: center;
  border: 1px solid rgba(244, 175, 65, 0.5);
  border-radius: 50%;
  color: var(--accent);
  font-weight: 800;
}

.trust-notice h2 {
  margin-bottom: 5px;
  font-size: 0.95rem;
}

.trust-notice p {
  margin-bottom: 0;
  color: var(--text-muted);
  line-height: 1.5;
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

  .eyebrow {
    font-size: 0.65rem;
  }

  .status-grid {
    grid-template-columns: 1fr;
  }

  footer {
    flex-wrap: wrap;
  }
}
</style>
