<script setup lang="ts">
import icon from '../assets/fuzzy-music-match-icon.svg';
</script>
<template>
  <header class="app-header"><RouterLink to="/tool-calls" class="brand"><span class="brand-icon" :style="{ maskImage: `url(${icon})` }" aria-hidden="true"></span>Lyrion Voice MCP</RouterLink><slot name="actions" /></header>
</template>
<style scoped>
.app-header { display:flex; align-items:center; gap:34px; min-height:65px; padding:0 26px; background:#15130f; color:#f6f0e5; flex-shrink:0; }
.brand { display:flex; align-items:center; gap:10px; color:inherit; font-size:22px; font-weight:650; text-decoration:none; white-space:nowrap; }.brand-icon { display:block; width:26px; height:36px; mask-size:contain; mask-position:center; mask-repeat:no-repeat; background:var(--amber); flex:none; }
@media(max-width:1050px) { .app-header { gap:24px; padding:0 18px; } }
@media(max-width:900px) { .app-header { flex-wrap:wrap; gap:12px 22px; padding:12px 16px; }.brand { font-size:20px; } }
@media(max-width:720px) { .app-header { display:grid; grid-template-columns:minmax(0,1fr) auto; gap:10px 18px; }.brand { grid-column:1/-1; } }
</style>
