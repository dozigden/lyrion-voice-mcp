<script setup lang="ts">
import type { ToolResults } from '../toolResults';
import ResultSection from '../components/ResultSection.vue';
import MediaList from '../components/MediaList.vue';
defineProps<{ result: ToolResults['search'] }>();
</script>
<template>
  <section v-if="result.exactArtistMatch" class="exact-section"><h3>Exact artist</h3>
    <div class="exact"><strong>{{ result.exactArtistMatch.name }}</strong><span v-if="result.exactArtistMatch.discographyAlbumCount !== null">{{ result.exactArtistMatch.discographyAlbumCount }} albums in discography</span><span v-else>Discography count not recorded</span></div>
  </section>
  <ResultSection v-if="result.artists.length" title="Artists" :count="result.artists.length"><MediaList :items="result.artists" /></ResultSection>
  <ResultSection title="Albums" :count="result.albums.length"><MediaList :items="result.albums" /></ResultSection>
  <ResultSection title="Top tracks" :count="result.topTracks.length"><MediaList :items="result.topTracks" /></ResultSection>
  <ResultSection title="Tracks" :count="result.tracks.length"><MediaList :items="result.tracks" vertical /></ResultSection>
  <ResultSection v-if="result.playlists.length" title="Playlists" :count="result.playlists.length"><MediaList :items="result.playlists" /></ResultSection>
  <div v-if="!result.artists.length || !result.playlists.length" class="empty-groups"><span v-if="!result.artists.length">Artists <span class="muted">0 returned</span></span><span v-if="!result.playlists.length">Playlists <span class="muted">0 returned</span></span></div>
</template>
<style scoped>
.exact-section { display:flex; align-items:baseline; gap:24px; margin:0 var(--detail-inset); padding:20px 0; border-bottom:1px solid var(--border); }.exact-section h3 { margin:0; white-space:nowrap; font-size:16px; }.empty-groups { display:flex; flex-wrap:wrap; gap:30px; margin:20px var(--detail-inset); font-size:14px; }.empty-groups .muted { margin-left:8px; }.exact { flex:1; display:flex; justify-content:space-between; flex-wrap:wrap; gap:10px 24px; }.exact strong { font-size:18px; }.exact span { color:var(--text-muted); font-size:14px; }
@media(max-width:720px) { .exact-section { flex-wrap:wrap; gap:8px 18px; } }
</style>
