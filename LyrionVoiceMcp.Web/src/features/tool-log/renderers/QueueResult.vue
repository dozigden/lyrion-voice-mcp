<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import MediaList from '../components/MediaList.vue';
defineProps<{ result: ToolResults['get_queue'] }>();
</script>
<template><ResultSection title="Queue" :count="result.items.length"><p>Player: {{ result.player }} · Current index: {{ result.currentIndex ?? 'Not recorded' }}</p><MediaList :items="result.items" :current-index="result.currentIndex" /></ResultSection></template>
