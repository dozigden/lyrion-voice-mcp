<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import PlayerState from '../components/PlayerState.vue';
defineProps<{ result: ToolResults['get_player_status'] }>();
</script>
<template><ResultSection title="Players" :count="result.players.length"><p v-if="!result.players.length" class="muted">No players returned.</p><div class="players" :class="{ horizontal: result.players.length <= 5 }"><PlayerState v-for="(player, index) in result.players" :key="index" :player="player" /></div></ResultSection></template>
<style scoped>.players { display:grid; gap:24px; }.horizontal { grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); }</style>
