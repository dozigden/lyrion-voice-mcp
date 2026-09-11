<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{ tool: string }>();
const tools = new Set([
  'search',
  'browse',
  'get_player_status',
  'control_player',
  'get_queue',
  'manage_queue',
  'play'
]);
const icon = computed(() => tools.has(props.tool) ? props.tool : 'unknown');
</script>

<template>
  <svg class="tool-icon" :class="`tool-icon--${icon}`" viewBox="0 0 24 24" aria-hidden="true">
    <template v-if="icon === 'search'">
      <circle cx="10.5" cy="10.5" r="6" />
      <path d="m15 15 4.5 4.5" />
    </template>
    <template v-else-if="icon === 'browse'">
      <circle cx="12" cy="12" r="9" />
      <path d="m15.5 8.5-2.2 4.8-4.8 2.2 2.2-4.8z" />
    </template>
    <template v-else-if="icon === 'get_player_status'">
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="M6.5 12h2l1.5-3 3 6 1.5-3h3" />
    </template>
    <template v-else-if="icon === 'control_player'">
      <path d="M6 4v16M12 4v16M18 4v16" />
      <circle cx="6" cy="9" r="2" />
      <circle cx="12" cy="15" r="2" />
      <circle cx="18" cy="8" r="2" />
    </template>
    <template v-else-if="icon === 'get_queue'">
      <path d="M8 6h12M8 12h12M8 18h12" />
      <circle cx="4" cy="6" r="1" class="filled" />
      <circle cx="4" cy="12" r="1" class="filled" />
      <circle cx="4" cy="18" r="1" class="filled" />
    </template>
    <template v-else-if="icon === 'manage_queue'">
      <path d="M4 6h9M4 12h7M4 18h9M18 10v8M14 14h8" />
    </template>
    <template v-else-if="icon === 'play'">
      <path d="m7 4 13 8L7 20z" class="filled" />
    </template>
    <template v-else>
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9a2.5 2.5 0 0 1 5 0c0 2-2.5 2-2.5 4" />
      <circle cx="12" cy="17" r="1" class="filled" />
    </template>
  </svg>
</template>

<style scoped>
.tool-icon { display:inline-block; width:1em; height:1em; flex:none; fill:none; stroke:currentColor; stroke-width:1.8; stroke-linecap:round; stroke-linejoin:round; }
.filled { fill:currentColor; stroke:none; }
</style>
