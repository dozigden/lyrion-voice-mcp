<script setup lang="ts">
import { seconds, label } from '../../../shared/format';
defineProps<{
  items: { title?: string; name?: string; artist?: string | null; album?: string | null; rating?: number; kind?: string; index?: number; durationSeconds?: number | null }[];
  vertical?: boolean; currentIndex?: number | null;
}>();
</script>
<template>
  <p v-if="!items.length" class="muted">None returned.</p>
  <table v-else-if="vertical" class="media-list vertical"><thead><tr><th scope="col">Title</th><th scope="col">Artist</th><th scope="col">Album</th><th scope="col">Rating</th></tr></thead><tbody><tr v-for="(item, index) in items" :key="index"><td><strong>{{ item.title ?? item.name }}</strong><span class="mobile-artist">{{ item.artist ?? 'Artist not recorded' }}</span></td><td>{{ item.artist ?? '—' }}</td><td>{{ item.album ?? '—' }}</td><td class="rating"><span aria-hidden="true">★ </span>{{ item.rating ?? '—' }}<span class="muted"> / 5</span></td></tr></tbody></table>
  <ul v-else class="media-list" :class="{ horizontal: !vertical && items.length <= 5, vertical: vertical || items.length > 5 }">
    <li v-for="(item, index) in items" :key="index" :class="{ current: item.index !== undefined && item.index === currentIndex }">
      <div class="identity"><strong>{{ item.title ?? item.name }}</strong><span v-if="item.kind" class="muted">{{ label(item.kind) }}</span><span v-if="item.index !== undefined" class="muted">Index {{ item.index }}<b v-if="item.index === currentIndex"> · Current</b></span></div>
      <span v-if="item.artist !== undefined" class="muted artist">{{ item.artist ?? 'Artist not recorded' }}</span>
      <span v-if="item.album !== undefined" class="muted album">{{ item.album ?? 'Album not recorded' }}</span>
      <span v-if="item.rating !== undefined" class="rating"><span aria-hidden="true">★ </span>{{ item.rating }}<span class="muted"> / 5</span></span>
      <span v-if="item.durationSeconds !== undefined" class="muted">{{ seconds(item.durationSeconds) }}</span>
    </li>
  </ul>
</template>
<style scoped>
table { width:100%; table-layout:fixed; border-collapse:collapse; text-align:left; } th { color:var(--text-muted); font-size:14px; font-weight:500; padding:0 10px 8px; border-bottom:1px solid var(--border); } td { padding:10px; font-size:14px; color:var(--text-muted); border-bottom:1px solid var(--border); overflow-wrap:anywhere; } td strong { color:var(--text); } th:first-child,td:first-child { padding-left:0; } th:first-child { width:30%; } th:last-child,td:last-child { text-align:right; padding-right:0; width:14%; } tbody tr:nth-child(even) { background:var(--stripe); }.mobile-artist { display:none; }
.media-list { list-style:none; padding:0; margin:0; }
.horizontal { display:grid; grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); gap:18px 0; }
.horizontal li { padding:0 15px; border-left:1px solid var(--border); min-width:0; }
.horizontal li:first-child { padding-left:0; border:0; }
li { overflow-wrap:anywhere; }
li > span, .identity > span { display:block; font-size:14px; margin-top:4px; }
strong { font-weight:600; }
.vertical li { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(0,1fr) minmax(0,1.2fr) auto; gap:8px 18px; padding:12px 8px; border-bottom:1px solid var(--border); }
.vertical li:nth-child(even) { background:var(--stripe); }
.vertical li > span { margin:0; align-self:center; }
.current { box-shadow:inset 3px 0 var(--selection); }
.rating { white-space:nowrap; font-size:14px; }
.rating > span:first-child { color:#855d16; }
@media(max-width:1050px) { .horizontal { grid-template-columns:repeat(auto-fit,minmax(145px,1fr)); } }
@media(max-width:720px) { th:nth-child(2),td:nth-child(2) { display:none; }.mobile-artist { display:block; color:var(--text-muted); font-weight:400; margin-top:4px; }th:first-child { width:42%; }th:last-child { width:18%; } .horizontal { grid-template-columns:repeat(2,minmax(0,1fr)); }.horizontal li:nth-child(odd) { padding-left:0; border:0; }.vertical li { grid-template-columns:minmax(0,1fr) auto; }.vertical .artist,.vertical .album { grid-column:1; } }
</style>
