import { createPinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './operationalHistoryApi';
import OperationalRecordListView from './OperationalRecordListView.vue';
import ScheduledJobsView from './ScheduledJobsView.vue';

describe('operational history views', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('pages through the complete durable job history', async () => {
    const list = vi.spyOn(api, 'listJobs')
      .mockResolvedValueOnce({
        items: [job(51)], total: 51, offset: 0, limit: 50, retentionDays: 90
      })
      .mockResolvedValueOnce({
        items: [job(1)], total: 51, offset: 50, limit: 50, retentionDays: 90
      });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/jobs', name: 'jobs', component: { template: '<div />' } },
        { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
      ]
    });
    await router.push('/jobs');
    await router.isReady();
    const wrapper = mount(OperationalRecordListView, {
      props: { kind: 'jobs' },
      global: { plugins: [createPinia(), router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('1–1 of 51');
    await wrapper.get('.pagination button:last-child').trigger('click');
    await flushPromises();

    expect(list).toHaveBeenNthCalledWith(1, '?offset=0&limit=50', expect.any(AbortSignal));
    expect(list).toHaveBeenNthCalledWith(2, '?offset=50&limit=50', expect.any(AbortSignal));
    expect(wrapper.text()).toContain('51–51 of 51');
    expect(wrapper.text()).toContain('#1 · test.work');
    expect(wrapper.text()).not.toContain('Operational history');
    expect(wrapper.text()).not.toContain('Inspect queued and completed work');
  });

  it('shows scheduler state and queues a run-now job', async () => {
    const list = vi.spyOn(api, 'listSchedules').mockResolvedValue([{
      name: 'catalogue-refresh',
      displayName: 'Catalogue refresh',
      enabled: false,
      cronExpression: '0 3 * * *',
      timeZoneId: 'Europe/London',
      lastEvaluatedAt: '2026-08-16T03:00:00Z',
      nextOccurrenceAt: null,
      currentJob: null,
      lastStartedJob: { id: 42, status: 'completed', startedAt: '2026-08-15T03:00:00Z' },
      editableConfiguration: {
        kind: 'daily_time', configuredEnabled: true, intervalMinutes: null, dailyTime: '03:00'
      }
    }]);
    const run = vi.spyOn(api, 'runSchedule').mockResolvedValue({ enqueuedCount: 1, jobIds: [7] });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scheduled-jobs', name: 'scheduled-jobs', component: ScheduledJobsView },
        { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
      ]
    });
    await router.push('/scheduled-jobs');
    await router.isReady();
    const wrapper = mount(ScheduledJobsView, { global: { plugins: [createPinia(), router] } });
    await flushPromises();

    expect(wrapper.text()).toContain('Next run');
    expect(wrapper.text()).not.toContain('Last evaluated');
    expect(wrapper.text()).not.toContain('Time zone');
    expect(wrapper.text()).toContain('#42 · completed');
    expect(wrapper.text()).toContain('Unavailable');
    await wrapper.get('button.run').trigger('click');
    await flushPromises();

    expect(run).toHaveBeenCalledWith('catalogue-refresh', expect.any(AbortSignal));
    expect(list).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).not.toContain('Operational automation');
    expect(wrapper.text()).not.toContain('Review every schedule');
  });

  it('edits, saves, and reloads an interval schedule', async () => {
    const initial = schedule('*/5 * * * *', 5, '2026-08-16T04:05:00Z');
    const refreshed = schedule('*/10 * * * *', 10, '2026-08-16T04:10:00Z');
    const list = vi.spyOn(api, 'listSchedules')
      .mockResolvedValueOnce([initial])
      .mockResolvedValueOnce([refreshed]);
    const update = vi.spyOn(api, 'updateScheduleConfiguration').mockResolvedValue(refreshed);
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scheduled-jobs', name: 'scheduled-jobs', component: ScheduledJobsView },
        { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
      ]
    });
    await router.push('/scheduled-jobs');
    await router.isReady();
    const wrapper = mount(ScheduledJobsView, { global: { plugins: [createPinia(), router] } });
    await flushPromises();

    expect(wrapper.find('.heading [role="status"]').exists()).toBe(false);
    const nextRun = () => wrapper.findAll('dl > div').find(field => field.get('dt').text() === 'Next run')!.get('dd').text();
    const initialNextRun = nextRun();
    await wrapper.get('select').setValue('10');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(update).toHaveBeenCalledWith('catalogue-change-check', {
      enabled: true, intervalMinutes: 10, dailyTime: null
    }, expect.any(AbortSignal));
    expect(list).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain('*/10 * * * *');
    expect(nextRun()).not.toBe(initialNextRun);
    expect(wrapper.get('select').attributes('aria-label'))
      .toBe('Check LMS catalogue changes interval');
    expect(wrapper.get('button.save').attributes('aria-label'))
      .toBe('Save Check LMS catalogue changes schedule');
    expect(wrapper.get('button.run').attributes('aria-label'))
      .toBe('Run Check LMS catalogue changes now');
  });

  it('keeps unsaved schedule drafts when Run now refreshes operational state', async () => {
    vi.spyOn(api, 'listSchedules').mockResolvedValue([schedule('*/5 * * * *', 5, null)]);
    vi.spyOn(api, 'runSchedule').mockResolvedValue({ enqueuedCount: 1, jobIds: [7] });
    const router = createRouter({ history: createMemoryHistory(), routes: [
      { path: '/scheduled-jobs', component: ScheduledJobsView },
      { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
    ] });
    await router.push('/scheduled-jobs');
    const wrapper = mount(ScheduledJobsView, { global: { plugins: [createPinia(), router] } });
    await flushPromises();
    await wrapper.get('select').setValue('15');
    await wrapper.get('button.run').trigger('click'); await flushPromises();
    expect((wrapper.get('select').element as HTMLSelectElement).value).toBe('15');
    wrapper.unmount();
  });
  it('keeps run-now available and reports save errors', async () => {
    vi.spyOn(api, 'listSchedules').mockResolvedValue([schedule('*/5 * * * *', 5, null)]);
    vi.spyOn(api, 'updateScheduleConfiguration')
      .mockRejectedValue(new Error('Choose a supported interval.'));
    const run = vi.spyOn(api, 'runSchedule').mockResolvedValue({ enqueuedCount: 1, jobIds: [7] });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scheduled-jobs', name: 'scheduled-jobs', component: ScheduledJobsView },
        { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
      ]
    });
    await router.push('/scheduled-jobs');
    await router.isReady();
    const wrapper = mount(ScheduledJobsView, { global: { plugins: [createPinia(), router] } });
    await flushPromises();

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('Choose a supported interval.');
    expect(wrapper.get('button.run').attributes('disabled')).toBeUndefined();
    await wrapper.get('button.run').trigger('click');
    await flushPromises();
    expect(run).toHaveBeenCalledWith('catalogue-change-check', expect.any(AbortSignal));
  });
});

function schedule(
  cronExpression: string,
  intervalMinutes: number,
  nextOccurrenceAt: string | null
): api.ScheduledJob {
  return {
    name: 'catalogue-change-check',
    displayName: 'Check LMS catalogue changes',
    enabled: true,
    cronExpression,
    timeZoneId: 'Europe/London',
    lastEvaluatedAt: '2026-08-16T04:00:00Z',
    nextOccurrenceAt,
    currentJob: null,
    lastStartedJob: null,
    editableConfiguration: {
      kind: 'interval', configuredEnabled: true, intervalMinutes, dailyTime: null
    }
  };
}

function job(id: number): api.JobSummary {
  return {
    id,
    type: 'test.work',
    status: 'completed',
    runAfter: '2026-08-16T03:00:00Z',
    startedAt: '2026-08-16T03:00:00Z',
    completedAt: '2026-08-16T03:00:01Z',
    correlationId: `test:${id}`,
    createdAt: '2026-08-16T03:00:00Z',
    updatedAt: '2026-08-16T03:00:01Z'
  };
}
