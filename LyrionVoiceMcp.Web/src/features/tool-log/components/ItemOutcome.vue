<script setup lang="ts">
import type { Outcome } from '../toolResults';
import ResultSection from './ResultSection.vue';
import { label } from '../../../shared/format';
defineProps<{ outcome: Outcome }>();
</script>
<template>
  <ResultSection title="Item outcome">
    <p>{{ outcome.completedItemCount }} of {{ outcome.requestedItemCount }} requested items completed.</p>
    <p v-if="outcome.stateRefreshError" class="notice error">State refresh: {{ outcome.stateRefreshError }}</p>
  </ResultSection>
  <ResultSection v-if="outcome.skippedItems.length" title="Skipped items" :count="outcome.skippedItems.length">
    <ul class="skipped" :class="{ horizontal: outcome.skippedItems.length <= 5 }">
      <li v-for="(item, index) in outcome.skippedItems" :key="index"><strong>Index {{ item.index }} · {{ label(item.reason) }}</strong><p>{{ item.message }}</p></li>
    </ul>
  </ResultSection>
</template>
<style scoped>
.skipped { list-style:none; padding:0; margin:0; display:grid; gap:18px; }
.horizontal { grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); }
li { overflow-wrap:anywhere; } li p { font-size:14px; color:var(--text-muted); margin:6px 0; }
</style>
