<template>
  <main class="page">
    <header><h1>Scheduled jobs</h1></header>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <div v-if="loading" class="empty">Loading schedules…</div>
    <section v-else class="grid">
      <article v-for="schedule in schedules" :key="schedule.name" class="card">
        <header class="heading">
          <div class="heading-identity">
            <h2>{{ schedule.displayName }}</h2>
            <span v-if="!schedule.enabled" class="tag" role="status">
              <span class="tag__dot" aria-hidden="true"></span>{{ statusLabel(schedule) }}
            </span>
          </div>
          <button
            class="run"
            type="button"
            :aria-label="`Run ${schedule.displayName} now`"
            :disabled="!!store.pending[schedule.name]"
            @click="run(schedule.name)"
          >
            {{ store.pending[schedule.name] === 'running' ? 'Queuing…' : 'Run now' }}
          </button>
        </header>

        <form
          v-if="schedule.editableConfiguration && drafts[schedule.name]"
          class="configuration"
          :aria-label="`${schedule.displayName} schedule configuration`"
          @submit.prevent="save(schedule)"
        >
          <label class="toggle">
            <input
              v-model="drafts[schedule.name].enabled"
              type="checkbox"
              :aria-label="`${schedule.displayName} schedule enabled`"
            >
            Schedule enabled
          </label>

          <div class="configuration-controls">
            <label v-if="schedule.editableConfiguration.kind === 'interval'" class="schedule-value">
              <select
                v-model="drafts[schedule.name].intervalMinutes"
                :aria-label="`${schedule.displayName} interval`"
              >
                <option :value="null" disabled>Choose an interval</option>
                <option v-for="minutes in intervals" :key="minutes" :value="minutes">
                  {{ intervalLabel(minutes) }}
                </option>
              </select>
            </label>

            <label v-else class="schedule-value">
              <input
                v-model="drafts[schedule.name].dailyTime"
                type="time"
                step="60"
                :aria-label="`${schedule.displayName} daily time`"
              >
            </label>
            <button
              class="save"
              type="submit"
              :aria-label="`Save ${schedule.displayName} schedule`"
              :disabled="!!store.pending[schedule.name] || needsSimpleValue(schedule)"
            >
              {{ store.pending[schedule.name] === 'saving' ? 'Saving…' : 'Save' }}
            </button>
          </div>

          <p v-if="needsSimpleValue(schedule)" class="hint">
            The deployment uses a custom cron expression. Choose a supported value to replace it.
          </p>
        </form>

        <dl>
          <div><dt>Next run</dt><dd>{{ formatOptional(schedule.nextOccurrenceAt) }}</dd></div>
          <div>
            <dt>Current job</dt>
            <dd>
              <RouterLink
                v-if="schedule.currentJob"
                :to="{ name: 'jobs-detail', params: { id: schedule.currentJob.id } }"
              >
                #{{ schedule.currentJob.id }} · {{ schedule.currentJob.status }}
              </RouterLink>
              <span v-else>—</span>
            </dd>
          </div>
          <div><dt>Cron expression</dt><dd><code>{{ schedule.cronExpression }}</code></dd></div>
          <div>
            <dt>Last job</dt>
            <dd>
              <RouterLink
                v-if="schedule.lastStartedJob"
                :to="{ name: 'jobs-detail', params: { id: schedule.lastStartedJob.id } }"
              >
                #{{ schedule.lastStartedJob.id }} · {{ schedule.lastStartedJob.status }}
              </RouterLink>
              <span v-else>—</span>
            </dd>
          </div>
        </dl>
      </article>
    </section>
  </main>
</template>

<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useSchedulesStore } from './schedulesStore';
import { RouterLink } from 'vue-router';
import type { ScheduledJob } from './operationalHistoryApi';

interface ScheduleDraft {
  enabled: boolean;
  intervalMinutes: number | null;
  dailyTime: string | null;
}

const intervals = [1, 5, 10, 15, 30, 60];
const store = useSchedulesStore();
const { schedules, loading, error } = storeToRefs(store);
const drafts = ref<Record<string, ScheduleDraft>>({});
onMounted(async () => { const result = await store.load(); result?.forEach(syncDraft); });
onBeforeUnmount(store.cancel);
async function save(schedule: ScheduledJob) {
  const draft = drafts.value[schedule.name];
  if (!draft) return;
  const refreshed = await store.save(schedule.name, { ...draft });
  if (refreshed) syncDraft(refreshed);
}
async function run(name: string) {
  // Running a job must not overwrite unsaved configuration drafts.
  await store.run(name);
}

function syncDraft(schedule: ScheduledJob) {
  const configuration = schedule.editableConfiguration;
  if (!configuration) return;

  drafts.value[schedule.name] = {
    enabled: configuration.configuredEnabled,
    intervalMinutes: configuration.intervalMinutes,
    dailyTime: configuration.dailyTime
  };
}

function needsSimpleValue(schedule: ScheduledJob) {
  const configuration = schedule.editableConfiguration;
  const draft = drafts.value[schedule.name];
  if (!configuration || !draft) return false;
  return configuration.kind === 'interval'
    ? draft.intervalMinutes === null
    : !draft.dailyTime;
}

function statusLabel(schedule: ScheduledJob) {
  if (schedule.editableConfiguration?.configuredEnabled) return 'Unavailable';
  return 'Disabled';
}

function intervalLabel(minutes: number) {
  return minutes === 1 ? 'Every minute' : `Every ${minutes} minutes`;
}

function formatOptional(value: string | null) {
  return value
    ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })
      .format(new Date(value))
    : '—';
}

</script>

<style scoped>
.page { width:min(1200px,100%); margin:0 auto; padding:22px 34px 32px; }
h1 { font-size:22px; margin:0; }
.grid { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); align-items:start; gap:24px 32px; margin-top:20px; }
.card { min-width:0; }
.heading { display:grid; grid-template-columns:minmax(0,1fr) auto; align-items:center; gap:12px; padding:8px 16px; background:var(--heading-band); color:var(--selection); }
.heading-identity { display:flex; align-items:center; flex-wrap:wrap; gap:4px 16px; min-width:0; }
h2 { margin:0; font-size:16px; overflow-wrap:anywhere; }
.tag { display:inline-flex; align-items:center; gap:6px; color:var(--text-muted); font-size:14px; }
.tag__dot { width:6px; height:6px; border-radius:50%; background:currentColor; flex:none; }
.configuration { display:flex; flex-wrap:wrap; align-items:flex-end; gap:12px 16px; margin:16px; }
.configuration label { display:grid; gap:4px; color:var(--text-muted); font-size:14px; }
.configuration .toggle { display:flex; align-items:center; gap:8px; min-height:35px; color:var(--text); }
input[type=checkbox] { accent-color:var(--selection); margin:0; }
.configuration-controls { display:grid; grid-template-columns:minmax(0,1fr) auto; align-items:end; gap:12px; flex:1 1 220px; min-width:0; }
.schedule-value { min-width:0; }
.schedule-value input,.schedule-value select { width:100%; min-width:0; height:35px; padding:6px 8px; font-size:14px; }
.hint { flex-basis:100%; font-size:14px; color:var(--text-muted); margin:0; }
dl { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:12px 24px; margin:16px; }
dt { font-size:14px; color:var(--text-muted); }
dd { margin:2px 0 0; overflow-wrap:anywhere; font-size:14px; }
code { font-size:14px; }
.save,.run { padding:6px 12px; font-size:14px; }
.save { background:var(--selection); border-color:var(--selection); color:#fff7ef; }
.run { border-color:var(--selection); color:var(--selection); background:transparent; white-space:nowrap; }
.empty { color:var(--text-muted); }
@media(max-width:1000px) { .grid { grid-template-columns:minmax(0,1fr); } }
@media(max-width:720px) {
  .page { padding:18px 18px 24px; }
  .grid { margin-top:16px; gap:20px; }
  .heading { padding:8px 12px; }
  .heading-identity { flex-direction:column; align-items:flex-start; }
  .configuration,dl { margin:12px; }
  .configuration .toggle { flex-basis:100%; min-height:0; }
  dl { gap:12px 16px; }
}
</style>
