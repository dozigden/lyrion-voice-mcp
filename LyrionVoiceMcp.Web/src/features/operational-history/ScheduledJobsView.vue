<template>
  <main class="page">
    <header><h1>Scheduled jobs</h1></header>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <div v-if="loading" class="empty">Loading schedules…</div>
    <section v-else class="grid">
      <article v-for="schedule in schedules" :key="schedule.name" class="card">
        <div class="heading">
          <div>
            <h2>{{ schedule.displayName }}</h2>
            <code>{{ schedule.cronExpression }}</code>
          </div>
          <span class="tag" :class="{ disabled: !schedule.enabled }">
            {{ statusLabel(schedule) }}
          </span>
        </div>

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

          <label v-if="schedule.editableConfiguration.kind === 'interval'">
            Check interval
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

          <label v-else>
            Daily time
            <input
              v-model="drafts[schedule.name].dailyTime"
              type="time"
              step="60"
              :aria-label="`${schedule.displayName} daily time`"
            >
          </label>

          <p v-if="needsSimpleValue(schedule)" class="hint">
            The deployment uses a custom cron expression. Choose a supported value to replace it.
          </p>
          <button
            class="save"
            type="submit"
            :aria-label="`Save ${schedule.displayName} schedule`"
            :disabled="!!store.pending[schedule.name] || needsSimpleValue(schedule)"
          >
            {{ store.pending[schedule.name] === 'saving' ? 'Saving…' : 'Save' }}
          </button>
        </form>

        <dl>
          <div><dt>Time zone</dt><dd>{{ schedule.timeZoneId }}</dd></div>
          <div><dt>Last evaluated</dt><dd>{{ formatOptional(schedule.lastEvaluatedAt) }}</dd></div>
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
          <div>
            <dt>Last started</dt>
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
        <button
          class="run"
          type="button"
          :aria-label="`Run ${schedule.displayName} now`"
          :disabled="!!store.pending[schedule.name]"
          @click="run(schedule.name)"
        >
          {{ store.pending[schedule.name] === 'running' ? 'Queuing…' : 'Run now' }}
        </button>
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
  if (schedule.enabled) return 'Enabled';
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
.page { width:min(1200px,100%); margin:0 auto; padding:28px 34px 40px; }h1 { font-size:22px; margin:0; }
.grid { display:grid; grid-template-columns:1fr 1fr; gap:28px 32px; margin-top:24px; }.card { min-width:0; padding-bottom:24px; border-bottom:1px solid var(--border); }
.heading { display:flex; justify-content:space-between; align-items:start; gap:14px; padding:12px 18px; background:var(--heading-band); color:var(--selection); }h2 { margin:0 0 6px; font-size:16px; }code,.tag { font-size:14px; }.tag { white-space:nowrap; }
.configuration { display:grid; gap:12px; margin:20px 0; }.configuration label { display:grid; gap:6px; color:var(--text-muted); font-size:14px; }.configuration .toggle { display:flex; align-items:center; gap:8px; color:var(--text); }input[type=checkbox] { accent-color:var(--selection); }.hint { font-size:14px; color:var(--text-muted); margin:0; }
dl { display:grid; grid-template-columns:1fr 1fr; gap:16px 24px; margin:20px 0; }dt { font-size:14px; color:var(--text-muted); }dd { margin:4px 0 0; overflow-wrap:anywhere; font-size:14px; }.save { justify-self:start; background:var(--selection); border-color:var(--selection); color:#fff7ef; }.run { border-color:var(--selection); color:var(--selection); background:transparent; }.empty { color:var(--text-muted); }
@media(max-width:760px) { .page { padding:24px 18px; }.grid { grid-template-columns:1fr; } }
</style>
