<script setup lang="ts">
import type { Player } from '../toolResults';
import { label, seconds } from '../../../shared/format';
defineProps<{ player: Player }>();
</script>
<template>
  <article class="player-state">
    <h4>{{ player.name }}</h4>
    <dl class="facts">
      <div><dt>Player ID</dt><dd>{{ player.id }}</dd></div>
      <div><dt>Power</dt><dd>{{ player.poweredOn ? 'On' : 'Off' }}</dd></div>
      <div><dt>Playback</dt><dd>{{ label(player.mode) }}</dd></div>
      <div><dt>Volume</dt><dd>{{ player.volume ?? 'Not recorded' }}</dd></div>
      <div><dt>Muted</dt><dd v-if="player.muted === null">Not recorded</dd><dd v-else>{{ player.muted ? 'Yes' : 'No' }}</dd></div>
    </dl>
    <div v-if="player.nowPlaying" class="now-playing">
      <h5>Now playing</h5><strong>{{ player.nowPlaying.title }}</strong>
      <p>{{ player.nowPlaying.artist ?? 'Artist not recorded' }} · {{ player.nowPlaying.album ?? 'Album not recorded' }}</p>
      <p>{{ seconds(player.nowPlaying.elapsedSeconds) }} / {{ seconds(player.nowPlaying.durationSeconds) }}</p>
    </div>
    <p v-else class="muted">No now-playing information recorded.</p>
  </article>
</template>
<style scoped>
h4 { margin:0 0 14px; font-size:18px; } h5 { font-size:14px; margin:18px 0 8px; color:var(--text-muted); }
.player-state { min-width:0; overflow-wrap:anywhere; }
.facts { display:flex; flex-wrap:wrap; gap:16px 26px; margin:0; }
dt { font-size:14px; color:var(--text-muted); } dd { margin:4px 0 0; }
.now-playing p { color:var(--text-muted); font-size:14px; margin:6px 0; }
</style>
