<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import PlayerState from '../components/PlayerState.vue';
import ItemOutcome from '../components/ItemOutcome.vue';
defineProps<{ result: ToolResults['play'] }>();
</script>
<template><ItemOutcome :outcome="result" /><ResultSection title="Player"><PlayerState v-if="result.player" :player="result.player" /><p v-else class="muted">Refreshed player state was not returned.</p></ResultSection></template>
