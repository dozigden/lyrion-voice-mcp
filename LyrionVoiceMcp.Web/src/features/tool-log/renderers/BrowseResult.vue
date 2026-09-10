<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import MediaList from '../components/MediaList.vue';
defineProps<{ result: ToolResults['browse'] }>();
</script>
<template><ResultSection title="Items" :count="result.items.length"><MediaList :items="result.items" /><p v-if="result.nextBrowseRef" class="muted">More items were available. The recorded continuation reference is below.</p><p v-else class="muted">No continuation reference returned.</p></ResultSection></template>
