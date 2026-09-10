<script setup lang="ts">
import { isRecord } from '../../../shared/api/decoder';
withDefaults(defineProps<{ value: unknown; depth?: number }>(), { depth: 0 });
</script>
<template>
  <span v-if="depth >= 12" class="muted">Further nested data is available in Raw JSON.</span>
  <ul v-else-if="Array.isArray(value)" class="values" :class="{ horizontal: value.length <= 5 }"><li v-for="(item, index) in value" :key="index"><RecordedValue :value="item" :depth="depth + 1" /></li><li v-if="!value.length" class="muted">None supplied</li></ul>
  <dl v-else-if="isRecord(value)"><div v-for="(item, key) in value" :key="key"><dt>{{ key }}</dt><dd><RecordedValue :value="item" :depth="depth + 1" /></dd></div></dl>
  <span v-else-if="value === null" class="muted">Not supplied</span>
  <span v-else-if="typeof value === 'boolean'">{{ value ? 'Yes' : 'No' }}</span>
  <span v-else>{{ value }}</span>
</template>
<style scoped>
.values { margin:0; padding:0; list-style:none; display:grid; gap:8px 24px; }.horizontal { display:flex; flex-wrap:wrap; } li { min-width:0; max-width:100%; } span { white-space:pre-wrap; overflow-wrap:anywhere; } dl { margin:0; } dt { color:var(--text-muted); font-size:14px; } dd { margin:4px 0 12px; }
</style>
