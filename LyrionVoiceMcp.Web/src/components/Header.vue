<script setup lang="ts">
</script>

<template>
  <header class="app-header">
    <RouterLink class="brand" to="/">Lyrion Voice MCP</RouterLink>
    <slot name="actions" />
  </header>
</template>

<style scoped>
.app-header { width: min(1180px, calc(100% - 40px)); margin: 0 auto; min-height: 64px; display: flex; align-items: center; justify-content: space-between; gap: 20px; border-bottom: 1px solid var(--border); }
.brand { color: var(--text); font: 600 1rem var(--font-display); text-decoration: none; }
@media (max-width: 540px) { .brand { display: none; } .app-header { justify-content: center; } }
</style>
