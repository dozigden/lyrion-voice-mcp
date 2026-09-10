<script setup lang="ts">
defineProps<{ title: string; count?: number }>();
</script>
<template>
  <section class="result-section">
    <h3>{{ title }}<span v-if="count !== undefined">{{ count }} returned</span></h3>
    <div class="section-content"><slot /></div>
  </section>
</template>
<style scoped>
.result-section { margin: 24px 0; }
h3 { display:flex; align-items:baseline; justify-content:space-between; gap:16px; margin:0 0 16px; padding:10px var(--detail-inset, 24px); background:var(--heading-band); color:var(--selection); font-size:16px; font-weight:650; }
h3 span { font-size:14px; font-weight:500; }
.section-content { padding:0 var(--detail-inset, 24px); }
</style>
