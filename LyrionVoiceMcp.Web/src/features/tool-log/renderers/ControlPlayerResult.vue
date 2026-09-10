<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import PlayerState from '../components/PlayerState.vue';
defineProps<{ result: ToolResults['control_player'] }>();
</script>
<template><ResultSection title="Player"><PlayerState :player="result.player" /></ResultSection></template>
