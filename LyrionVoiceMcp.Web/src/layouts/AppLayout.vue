<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue';
import { useRoute } from 'vue-router';
import Header from '../components/Header.vue';
import { useOperationsStore } from '../features/operations/operationsStore';
import { catalogueHeaderStatus, currentIndexHeaderStatus, indexHeaderStatus } from '../features/operations/headlineStatus';
import { formatDate } from '../shared/format';
const route = useRoute(), operations = useOperationsStore();
const isLog = computed(() => route.path.startsWith('/tool-calls'));
const isSystem = computed(() => route.path.startsWith('/system'));
const isSystemList = computed(() => ['jobs', 'errors', 'scheduled-jobs'].includes(String(route.name)));
const now = ref(Date.now());
const catalogue = computed(() => catalogueHeaderStatus(
  operations.catalogue, operations.catalogueLoading, operations.catalogueErrorMessage, now.value));
const index = computed(() => indexHeaderStatus(
  operations.searchIndex, operations.searchIndexesLoading, operations.searchIndexesErrorMessage, now.value));
const currentIndex = computed(() => currentIndexHeaderStatus(operations.searchIndex, now.value));
const indexRebuilding = computed(() => index.value.working);
const catalogueRebuilding = computed(() => catalogue.value.working && !indexRebuilding.value);
const displayedIndex = computed(() => indexRebuilding.value ? currentIndex.value : index.value);
const controller = new AbortController();
let clock: number | undefined;
onMounted(() => {
  clock = window.setInterval(() => { now.value = Date.now(); }, 60_000);
  if (route.name !== 'home') {
    void operations.loadCatalogue(controller.signal);
    void operations.loadSearchIndexes(controller.signal);
  }
});
onBeforeUnmount(() => {
  controller.abort();
  if (clock !== undefined) window.clearInterval(clock);
});
</script>
<template>
  <div class="app-shell" :class="{ 'log-shell': isLog, 'system-list-shell': isSystemList }">
    <Header><template #actions>
      <nav class="app-nav" aria-label="Main navigation"><RouterLink to="/tool-calls" :class="{ active: isLog }" :aria-current="isLog ? 'page' : undefined">Tool log</RouterLink><RouterLink to="/system" :class="{ active: isSystem }" :aria-current="isSystem ? 'page' : undefined">System</RouterLink></nav>
      <div class="health" aria-label="System status">
        <RouterLink class="pipeline-status" :class="{ 'pipeline-status--three': indexRebuilding }" to="/system">
          <span class="pipeline-segment" :class="{ 'pipeline-segment--active': catalogueRebuilding }">
            <span class="pipeline-name"><span class="dot" :class="{ ready: catalogue.ready }" aria-hidden="true"></span>Catalogue</span>
            <time v-if="catalogue.timestamp" :datetime="catalogue.timestamp" :title="formatDate(catalogue.timestamp)">{{ catalogue.label }}</time>
            <span v-else class="pipeline-value">{{ catalogue.label }}</span>
          </span>
          <span v-if="indexRebuilding" class="pipeline-segment pipeline-segment--active">
            <span class="pipeline-name"><span class="dot" aria-hidden="true"></span>Index build</span>
            <span class="pipeline-value">Rebuilding</span>
          </span>
          <span class="pipeline-segment" :class="{ 'pipeline-segment--after-active': catalogueRebuilding || indexRebuilding }">
            <span class="pipeline-name"><span class="dot" :class="{ ready: displayedIndex.ready }" aria-hidden="true"></span>Index</span>
            <time v-if="displayedIndex.timestamp" :datetime="displayedIndex.timestamp" :title="formatDate(displayedIndex.timestamp)">{{ displayedIndex.label }}</time>
            <span v-else class="pipeline-value">{{ displayedIndex.label }}</span>
          </span>
        </RouterLink>
      </div>
      <RouterLink class="licences-link" to="/licences">Licences</RouterLink>
    </template></Header>
    <nav v-if="isSystem" class="system-nav" aria-label="System navigation"><RouterLink to="/system" exact-active-class="selected">Overview</RouterLink><RouterLink to="/system/jobs">Jobs</RouterLink><RouterLink to="/system/schedules">Schedules</RouterLink><RouterLink to="/system/errors">Errors</RouterLink></nav>
    <RouterView />
  </div>
</template>
<style scoped>
.app-shell { min-height:100dvh; display:flex; flex-direction:column; }.log-shell,.system-list-shell { height:100dvh; overflow:hidden; }
.app-nav { display:flex; align-self:stretch; align-items:stretch; gap:26px; }.app-nav a { display:flex; align-items:center; color:#b9b1a3; text-decoration:none; border-bottom:4px solid transparent; padding:14px 0 10px; }.app-nav a.active { color:#fff7ef; border-bottom-color:#c89bbd; }.app-nav a:hover { color:#fff7ef; }
.health { margin-left:auto; min-width:0; font-size:14px; }
.pipeline-status { display:grid; grid-template-columns:repeat(2,minmax(108px,1fr)); overflow:hidden; isolation:isolate; color:#211b12; text-decoration:none; background:var(--amber); border:1px solid var(--amber); border-radius:999px; }
.pipeline-status--three { grid-template-columns:repeat(3,minmax(108px,1fr)); }
.pipeline-status:hover { border-color:#f0ce8d; }
.pipeline-segment { position:relative; display:grid; gap:2px; min-width:108px; padding:6px 16px 7px; }
.pipeline-segment + .pipeline-segment { border-left:1px solid rgba(33,27,18,.38); }
.pipeline-segment.pipeline-segment--after-active { border-left:0; padding-left:28px; }
.pipeline-name { display:flex; align-items:center; gap:7px; color:#211b12; font-size:13px; font-weight:650; line-height:1.15; white-space:nowrap; }
.pipeline-segment time,.pipeline-value { color:#5d4523; font-size:12px; line-height:1.15; white-space:nowrap; }
.pipeline-segment--active { z-index:1; background:#c7953d; }
.pipeline-segment--active::before { content:""; position:absolute; z-index:0; top:0; right:-12px; width:12px; height:100%; background:#c7953d; clip-path:polygon(0 0,100% 50%,0 100%); }
.pipeline-segment--active::after { content:""; position:absolute; z-index:1; top:0; left:-12px; width:24px; height:100%; background:var(--amber); clip-path:polygon(0 0,12px 0,100% 50%,12px 100%,0 100%,12px 50%); animation:pipeline-flow 1400ms linear infinite; }
.pipeline-segment--active > * { position:relative; z-index:2; }
.pipeline-segment--active .pipeline-value { color:#3d2c14; }
.dot { width:6px; height:6px; border-radius:50%; background:#805500; flex:none; }
.dot.ready { background:#45654a; }
.licences-link { font-size:14px; color:#b9b1a3; text-decoration:none; }.licences-link:hover { color:#fff7ef; text-decoration:underline; }
.system-nav { display:flex; flex-shrink:0; gap:28px; padding:0 34px; border-bottom:1px solid var(--border); background:var(--sidebar); }.system-nav a { padding:15px 0 11px; color:var(--text-muted); text-decoration:none; border-bottom:2px solid transparent; }.system-nav a.selected,.system-nav a.router-link-active:not(:first-child) { color:var(--selection); border-bottom-color:var(--selection); }
@media(max-width:900px) { .app-nav { margin-left:auto; }.health { margin-left:0; flex:1; }.app-nav a { padding:0 0 6px; }.licences-link { margin-left:auto; } }
@media(max-width:400px) { .brand { width:100%; }.pipeline-status { width:100%; grid-template-columns:repeat(2,minmax(0,1fr)); }.pipeline-status--three { grid-template-columns:repeat(3,minmax(0,1fr)); }.pipeline-segment { min-width:0; padding-inline:10px; }.pipeline-segment.pipeline-segment--after-active { padding-left:20px; }.system-nav { padding:0 18px; gap:20px; } }
@media(max-width:720px) { .app-nav { grid-row:2; grid-column:1; margin:0; justify-self:start; gap:24px; }.licences-link { grid-row:2; grid-column:2; margin:0; }.health { grid-row:3; grid-column:1/-1; } }
@media(max-height:520px) { .system-list-shell { height:auto; overflow:visible; } }
@media(prefers-reduced-motion:reduce) { .pipeline-segment--active::after { animation:none; opacity:.45; left:calc(50% - 12px); } }
@keyframes pipeline-flow { 0% { left:-12px; opacity:0; } 10%,95% { opacity:1; } 100% { left:calc(100% - 12px); opacity:0; } }
</style>
