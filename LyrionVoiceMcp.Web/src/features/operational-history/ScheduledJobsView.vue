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
            :disabled="saving === schedule.name || needsSimpleValue(schedule)"
          >
            {{ saving === schedule.name ? 'Saving…' : 'Save' }}
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
          :disabled="running === schedule.name"
          @click="run(schedule.name)"
        >
          {{ running === schedule.name ? 'Queuing…' : 'Run now' }}
        </button>
      </article>
    </section>
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import {
  listSchedules,
  runSchedule,
  updateScheduleConfiguration,
  type ScheduledJob
} from './operationalHistoryApi';

interface ScheduleDraft {
  enabled: boolean;
  intervalMinutes: number | null;
  dailyTime: string | null;
}

const intervals = [1, 5, 10, 15, 30, 60];
const schedules = ref<ScheduledJob[]>([]);
const drafts = ref<Record<string, ScheduleDraft>>({});
const loading = ref(false);
const running = ref<string | null>(null);
const saving = ref<string | null>(null);
const error = ref<string | null>(null);

onMounted(load);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    schedules.value = await listSchedules();
    schedules.value.forEach(syncDraft);
  } catch (reason) {
    error.value = errorMessage(reason, 'Schedules could not be loaded.');
  } finally {
    loading.value = false;
  }
}

async function save(schedule: ScheduledJob) {
  const draft = drafts.value[schedule.name];
  if (!draft) return;

  saving.value = schedule.name;
  error.value = null;
  try {
    await updateScheduleConfiguration(schedule.name, draft);
    await reloadSchedule(schedule.name);
  } catch (reason) {
    error.value = errorMessage(reason, 'The schedule could not be saved.');
  } finally {
    saving.value = null;
  }
}

async function run(name: string) {
  running.value = name;
  error.value = null;
  try {
    await runSchedule(name);
    await reloadSchedule(name);
  } catch (reason) {
    error.value = errorMessage(reason, 'The job could not be queued.');
  } finally {
    running.value = null;
  }
}

async function reloadSchedule(name: string) {
  const refreshed = await listSchedules();
  const schedule = refreshed.find(item => item.name === name);
  if (!schedule) throw new Error('The updated schedule was not returned.');

  const index = schedules.value.findIndex(item => item.name === name);
  if (index >= 0) schedules.value.splice(index, 1, schedule);
  syncDraft(schedule);
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

function errorMessage(reason: unknown, fallback: string) {
  return reason instanceof Error ? reason.message : fallback;
}
</script>

<style scoped>
.page { width: min(1180px, calc(100% - 40px)); margin: 0 auto; padding: 48px 0 64px; }
h1 { margin: 0; font: 620 clamp(2.2rem, 5vw, 4rem) / 1 var(--font-display); }
.grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; margin-top: 26px; }
.card { padding: 20px; border: 1px solid var(--border); border-radius: 16px; background: var(--surface); }
.heading { display: flex; justify-content: space-between; gap: 14px; }
h2 { margin: 0 0 8px; font-size: 1.1rem; }
code { color: var(--accent-soft); }
.tag { height: max-content; padding: 5px 8px; border-radius: 999px; color: var(--success); background: rgba(95, 211, 151, .08); font-size: .72rem; }
.tag.disabled { color: var(--text-muted); background: rgba(255, 255, 255, .05); }
.configuration { display: grid; gap: 12px; margin: 20px 0; padding: 15px; border: 1px solid var(--border); border-radius: 12px; }
.configuration label { display: grid; gap: 6px; color: var(--text-dim); font-size: .78rem; }
.configuration .toggle { display: flex; align-items: center; gap: 8px; color: var(--text); }
select, input[type="time"] { padding: 9px 10px; border: 1px solid var(--border); border-radius: 8px; color: var(--text); background: #211f19; font: inherit; }
.hint { margin: 0; color: var(--text-muted); font-size: .78rem; line-height: 1.4; }
dl { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin: 20px 0; }
dt { color: var(--text-dim); font-size: .72rem; }
dd { margin: 5px 0 0; font-size: .84rem; }
a { color: var(--accent); }
button { padding: 10px 13px; border: 1px solid var(--accent); border-radius: 9px; font: inherit; font-weight: 700; cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .58; }
.save { justify-self: start; color: #21170a; background: var(--accent); }
.run { color: var(--accent); background: transparent; }
.error { color: var(--danger-text); }
.empty { color: var(--text-muted); }
@media (max-width: 760px) { .grid { grid-template-columns: 1fr; } }
</style>
