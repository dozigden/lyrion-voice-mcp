<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount } from 'vue';
import { useRoute } from 'vue-router';
import Header from '../components/Header.vue';
import { useOperationsStore } from '../features/operations/operationsStore';
import { catalogueHeadline, indexHeadline } from '../features/operations/headlineStatus';
const route = useRoute(), operations = useOperationsStore();
const isLog = computed(() => route.path.startsWith('/tool-calls'));
const isSystem = computed(() => route.path.startsWith('/system'));
const catalogue = computed(() => catalogueHeadline(operations.catalogue, operations.catalogueLoading, operations.catalogueErrorMessage));
const index = computed(() => indexHeadline(operations.searchIndex, operations.searchIndexesLoading, operations.searchIndexesErrorMessage));
const controller = new AbortController();
onMounted(() => {
  if (route.name !== 'home') {
    void operations.loadCatalogue(controller.signal);
    void operations.loadSearchIndexes(controller.signal);
  }
});
onBeforeUnmount(() => controller.abort());
</script>
<template>
  <div class="app-shell" :class="{ 'log-shell': isLog }">
    <Header><template #actions>
      <nav class="app-nav" aria-label="Main navigation"><RouterLink to="/tool-calls" :class="{ active: isLog }" :aria-current="isLog ? 'page' : undefined">Tool log</RouterLink><RouterLink to="/system" :class="{ active: isSystem }" :aria-current="isSystem ? 'page' : undefined">System</RouterLink></nav>
      <div class="health" aria-label="System status"><RouterLink to="/system"><span class="dot" :class="{ ready: catalogue === 'Ready' }" aria-hidden="true"></span>Catalogue <span>{{ catalogue }}</span></RouterLink><RouterLink to="/system"><span class="dot" :class="{ ready: index === 'Ready' }" aria-hidden="true"></span>Index <span>{{ index }}</span></RouterLink></div>
      <RouterLink class="licences-link" to="/licences">Licences</RouterLink>
    </template></Header>
    <nav v-if="isSystem" class="system-nav" aria-label="System navigation"><RouterLink to="/system" exact-active-class="selected">Overview</RouterLink><RouterLink to="/system/jobs">Jobs</RouterLink><RouterLink to="/system/schedules">Schedules</RouterLink><RouterLink to="/system/errors">Errors</RouterLink></nav>
    <RouterView />
  </div>
</template>
<style scoped>
.app-shell { min-height:100dvh; display:flex; flex-direction:column; }.log-shell { height:100dvh; overflow:hidden; }
.app-nav { display:flex; align-self:stretch; align-items:stretch; gap:26px; }.app-nav a { display:flex; align-items:center; color:#b9b1a3; text-decoration:none; border-bottom:4px solid transparent; padding:14px 0 10px; }.app-nav a.active { color:#fff7ef; border-bottom-color:#c89bbd; }.app-nav a:hover { color:#fff7ef; }
.health { margin-left:auto; display:flex; flex-wrap:wrap; gap:20px; font-size:14px; }.health a { display:flex; align-items:center; gap:7px; color:#f6f0e5; text-decoration:none; }.health a > span:last-child { color:#b9b1a3; }.dot { width:6px; height:6px; border-radius:50%; background:var(--amber); }.dot.ready { background:#88a28a; }
.licences-link { font-size:14px; color:#b9b1a3; text-decoration:none; }.licences-link:hover { color:#fff7ef; text-decoration:underline; }
.system-nav { display:flex; gap:28px; padding:0 34px; border-bottom:1px solid var(--border); background:var(--sidebar); }.system-nav a { padding:15px 0 11px; color:var(--text-muted); text-decoration:none; border-bottom:2px solid transparent; }.system-nav a.selected,.system-nav a.router-link-active:not(:first-child) { color:var(--selection); border-bottom-color:var(--selection); }
@media(max-width:900px) { .app-nav { margin-left:auto; }.health { margin-left:0; flex:1; }.app-nav a { padding:0 0 6px; }.licences-link { margin-left:auto; } }
@media(max-width:400px) { .brand { width:100%; }.health { gap:4px 14px; }.system-nav { padding:0 18px; gap:20px; } }
@media(max-width:720px) { .app-nav { grid-row:2; grid-column:1; margin:0; justify-self:start; gap:24px; }.licences-link { grid-row:2; grid-column:2; margin:0; }.health { grid-row:3; grid-column:1/-1; gap:6px 20px; } }
</style>
