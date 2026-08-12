<template>
  <header class="app-header">
    <RouterLink class="brand" to="/">Lyrion Voice MCP</RouterLink>
    <nav aria-label="Primary navigation">
      <RouterLink to="/">Status</RouterLink>
      <RouterLink to="/search-observations">Search observations</RouterLink>
    </nav>
  </header>
  <RouterView />
</template>

<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router';
</script>

<style scoped>
.app-header { width: min(1180px, calc(100% - 40px)); margin: 0 auto; min-height: 64px; display: flex; align-items: center; justify-content: space-between; gap: 20px; border-bottom: 1px solid var(--border); }
.brand { color: var(--text); font: 600 1rem var(--font-display); text-decoration: none; }
nav { display: flex; gap: 7px; }
nav a { padding: 8px 10px; border-radius: 8px; color: var(--text-muted); font-size: .82rem; text-decoration: none; }
nav a:hover, nav a.router-link-active { color: var(--accent); background: rgba(244,175,65,.07); }
@media (max-width: 540px) { .brand { display: none; } .app-header { justify-content: center; } }
</style>
