<script setup lang="ts">
import Header from '../components/Header.vue';
</script>

<template>
  <Header>
    <template #actions>
      <nav aria-label="Primary navigation" class="app-nav">
        <RouterLink to="/">Status</RouterLink>
        <RouterLink to="/search-observations">Search observations</RouterLink>
        <RouterLink to="/jobs">Jobs</RouterLink>
        <RouterLink to="/scheduled-jobs">Schedules</RouterLink>
        <RouterLink to="/errors">Errors</RouterLink>
        <RouterLink to="/tool-calls">MCP calls</RouterLink>
        <RouterLink to="/licences">Licences</RouterLink>
      </nav>
    </template>
  </Header>
  <div>
    <RouterView />
  </div>
</template>

<style scoped>
.app-nav { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 7px; }
.app-nav a { padding: 8px 10px; border-radius: 8px; color: var(--text-muted); font-size: .82rem; text-decoration: none; }
.app-nav a:hover, .app-nav a.router-link-active { color: var(--accent); background: rgba(244,175,65,.07); }
</style>
