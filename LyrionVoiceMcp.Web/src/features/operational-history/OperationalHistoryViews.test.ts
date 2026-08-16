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
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('1–1 of 51');
    await wrapper.get('.pagination button:last-child').trigger('click');
    await flushPromises();

    expect(list).toHaveBeenNthCalledWith(1, '?offset=0&limit=50');
    expect(list).toHaveBeenNthCalledWith(2, '?offset=50&limit=50');
    expect(wrapper.text()).toContain('51–51 of 51');
    expect(wrapper.text()).toContain('#1 · test.work');
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
      lastStartedJob: { id: 42, status: 'completed', startedAt: '2026-08-15T03:00:00Z' }
    }]);
    const run = vi.spyOn(api, 'runSchedule').mockResolvedValue({});
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scheduled-jobs', name: 'scheduled-jobs', component: ScheduledJobsView },
        { path: '/jobs/:id', name: 'jobs-detail', component: { template: '<div />' } }
      ]
    });
    await router.push('/scheduled-jobs');
    await router.isReady();
    const wrapper = mount(ScheduledJobsView, { global: { plugins: [router] } });
    await flushPromises();

    expect(wrapper.text()).toContain('Last evaluated');
    expect(wrapper.text()).toContain('#42 · completed');
    await wrapper.get('button').trigger('click');
    await flushPromises();

    expect(run).toHaveBeenCalledWith('catalogue-refresh');
    expect(list).toHaveBeenCalledTimes(2);
  });
});

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
