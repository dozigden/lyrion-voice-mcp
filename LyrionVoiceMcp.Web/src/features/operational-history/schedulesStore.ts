import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { remoteResource } from '../../shared/state/remoteResource';
import * as api from './operationalHistoryApi';
export const useSchedulesStore = defineStore('schedules', () => {
  const list = remoteResource<api.ScheduledJob[]>();
  const pending = ref<Record<string, 'saving' | 'running'>>({});
  const mutationError = ref<string | null>(null);
  const controllers = new Map<string, AbortController>();
  const schedules = computed(() => list.data.value ?? []);
  const error = computed(() => mutationError.value ?? list.error.value);
  async function load() { return list.load(api.listSchedules); }
  async function mutate(name: string, kind: 'saving' | 'running', update?: api.ScheduledJobConfigurationUpdate) {
    if (controllers.has(name)) return null;
    const controller = new AbortController();
    controllers.set(name, controller); pending.value[name] = kind; mutationError.value = null;
    try {
      if (kind === 'saving' && update) await api.updateScheduleConfiguration(name, update, controller.signal);
      else await api.runSchedule(name, controller.signal);
      if (controller.signal.aborted) return null;
      const refreshed = await api.listSchedules(controller.signal);
      if (controller.signal.aborted) return null;
      const schedule = refreshed.find(item => item.name === name);
      if (!schedule) throw new Error('The updated schedule was not returned.');
      list.data.value = schedules.value.map(item => item.name === name ? schedule : item);
      return schedule;
    } catch (reason) {
      if (!controller.signal.aborted) mutationError.value = reason instanceof Error ? reason.message : 'The schedule could not be updated.';
      return null;
    } finally {
      if (controllers.get(name) === controller) { controllers.delete(name); delete pending.value[name]; }
    }
  }
  const save = (name: string, update: api.ScheduledJobConfigurationUpdate) => mutate(name, 'saving', update);
  const run = (name: string) => mutate(name, 'running');
  function cancel() { list.cancel(); controllers.forEach(controller => controller.abort()); controllers.clear(); pending.value = {}; }
  return { schedules, loading: list.loading, error, pending, load, save, run, cancel };
});
