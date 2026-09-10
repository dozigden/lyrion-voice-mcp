<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import ItemOutcome from '../components/ItemOutcome.vue';
defineProps<{ result: ToolResults['manage_queue'] }>();
</script>
<template><ResultSection title="Queue"><p>Player: {{ result.player }}</p><p>Queue length: {{ result.queueLength ?? 'Not recorded' }}</p></ResultSection><ItemOutcome :outcome="result" /></template>
