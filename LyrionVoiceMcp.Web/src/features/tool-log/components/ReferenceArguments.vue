<script setup lang="ts">
import type { ToolCallReferenceSnapshot } from '../../operational-history/operationalHistoryApi';
import { label } from '../../../shared/format';

defineProps<{ items: ToolCallReferenceSnapshot[] }>();
</script>

<template>
  <ul class="references" :class="{ horizontal: items.length <= 5, vertical: items.length > 5 }">
    <li v-for="item in items" :key="item.argumentPath">
      <strong>{{ item.displayMetadata?.title ?? 'Name not captured' }}</strong>
      <span v-if="item.displayMetadata" class="muted">
        {{ label(item.displayMetadata.kind) }}<template v-if="item.displayMetadata.isContinuation"> · Continued</template>
      </span>
      <span v-if="item.displayMetadata?.artist" class="muted">{{ item.displayMetadata.artist }}</span>
      <span v-if="item.displayMetadata?.album" class="muted">{{ item.displayMetadata.album }}</span>
      <code>{{ item.reference }}</code>
    </li>
  </ul>
</template>

<style scoped>
.references { list-style:none; padding:0; margin:0; }
.horizontal { display:flex; flex-wrap:wrap; gap:18px 0; }
.horizontal li { flex:1 1 145px; padding:0 15px; border-left:1px solid var(--border); }
.horizontal li:first-child { padding-left:0; border-left:0; }
.vertical li { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(0,1fr) minmax(0,1fr); gap:5px 18px; padding:11px 8px; border-bottom:1px solid var(--border); }
.vertical li:nth-child(even) { background:var(--stripe); }
.vertical strong,.vertical code { grid-column:1; }
li { min-width:0; overflow-wrap:anywhere; }
li > span,code { display:block; margin-top:4px; font-size:14px; }
strong { font-weight:600; }
code { color:var(--text-muted); font-size:12px; white-space:normal; overflow-wrap:anywhere; }
@media(max-width:720px) { .horizontal li { flex-basis:50%; }.vertical li { grid-template-columns:minmax(0,1fr); }.vertical strong,.vertical code { grid-column:auto; } }
</style>
