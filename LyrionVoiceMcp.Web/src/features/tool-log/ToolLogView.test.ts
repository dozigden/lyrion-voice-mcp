import { createPinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ToolLogView from './ToolLogView.vue';
import * as api from '../operational-history/operationalHistoryApi';
import { call, summary } from './toolLogFixtures';
const wrappers: ReturnType<typeof mount>[] = [];
async function open(path = '/tool-calls') {
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/tool-calls', name: 'tool-calls', component: ToolLogView },
    { path: '/tool-calls/:id', name: 'tool-calls-detail', component: ToolLogView },
    { path: '/elsewhere', component: { template: '<p>Elsewhere</p>' } }
  ] });
  await router.push(path);
  const wrapper = mount(defineComponent({ template: '<RouterView />' }), { global: { plugins: [createPinia(), router] } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, router };
}
describe('tool log navigation', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(api, 'listToolCalls').mockResolvedValue({ items: [summary('first'), summary('second')], total: 52, offset: 0, limit: 50, retentionDays: 30 });
    vi.spyOn(api, 'getToolCall').mockImplementation(async id => call('search', id));
  });
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); });
  it('selects the newest call without fetching details for every list row or hiding the mobile list', async () => {
    const { wrapper, router } = await open();
    expect(router.currentRoute.value.params.id).toBe('first');
    expect(api.getToolCall).toHaveBeenCalledTimes(1);
    expect(wrapper.get('.workspace').classes()).not.toContain('mobile-detail');
    expect(wrapper.text()).not.toContain('Retained');
    expect(wrapper.get('.call-row.selected').text()).toContain('search');
    expect(wrapper.find('.apply').exists()).toBe(false);
    expect(wrapper.find('.row-outcome').exists()).toBe(false);
    expect(wrapper.get('.log-pane').text()).not.toContain('420 ms');
  });
  it('shows bounded request summaries directly from the list and leaves unavailable rows compact', async () => {
    const text = 'Paper Satellites · Genre: Jazz · Years: 1990–2000';
    vi.mocked(api.listToolCalls).mockResolvedValue({ items: [
      { ...summary('first'), requestSummary: text },
      { ...summary('second'), requestSummary: null }
    ], total: 2, offset: 0, limit: 50, retentionDays: 30 });
    const { wrapper } = await open();
    const rows = wrapper.findAll('.call-row');
    expect(rows[0]!.get('.row-summary').text()).toBe(text);
    expect(rows[0]!.get('.row-summary').attributes('title')).toBe(text);
    expect(rows[1]!.find('.row-summary').exists()).toBe(false);
    expect(api.getToolCall).toHaveBeenCalledTimes(1);
  });
  it('honours direct links outside the current page, reuses the pane, preserves scroll and supports Back', async () => {
    const { wrapper, router } = await open('/tool-calls/direct');
    expect(api.getToolCall).toHaveBeenCalledWith('direct', expect.any(AbortSignal));
    const list = wrapper.get('.log-list').element;
    list.scrollTop = 240; await wrapper.get('.log-list').trigger('scroll');
    await wrapper.findAll('.call-row')[1]!.trigger('click'); await flushPromises();
    expect(router.currentRoute.value.params.id).toBe('second');
    expect(list.scrollTop).toBe(240);
    expect(api.listToolCalls).toHaveBeenCalledTimes(1);
    router.back(); await flushPromises();
    expect(router.currentRoute.value.params.id).toBe('direct');
  });
  it('selects the first returned call when filters or page changes and handles an empty page', async () => {
    const { wrapper, router } = await open();
    vi.mocked(api.listToolCalls).mockResolvedValueOnce({ items: [summary('older')], total: 52, offset: 50, limit: 50, retentionDays: 30 });
    await wrapper.get('[aria-label="Next page"]').trigger('click'); await flushPromises();
    expect(router.currentRoute.value.params.id).toBe('older');
    expect(api.listToolCalls).toHaveBeenLastCalledWith('?offset=50&limit=50', expect.any(AbortSignal));
    vi.mocked(api.listToolCalls).mockResolvedValueOnce({ items: [], total: 0, offset: 0, limit: 50, retentionDays: 30 });
    await wrapper.get('input').setValue('play'); await flushPromises();
    expect(router.currentRoute.value.params.id).toBeUndefined();
    expect(wrapper.text()).toContain('No calls match these filters.');
    expect(wrapper.find('.call-heading').exists()).toBe(false);
  });
  it('applies filters immediately and shows only non-success outcomes in rows', async () => {
    vi.mocked(api.listToolCalls).mockResolvedValue({ items: [
      summary('first'),
      { ...summary('second'), status: 'failed' }
    ], total: 2, offset: 0, limit: 50, retentionDays: 30 });
    const { wrapper, router } = await open();

    await wrapper.get('input').setValue('play'); await flushPromises();
    expect(router.currentRoute.value.query.toolName).toBe('play');
    expect(api.listToolCalls).toHaveBeenLastCalledWith('?offset=0&limit=50&toolName=play', expect.any(AbortSignal));
    await wrapper.get('select').setValue('failed'); await flushPromises();
    expect(router.currentRoute.value.query.status).toBe('failed');
    expect(api.listToolCalls).toHaveBeenLastCalledWith('?offset=0&limit=50&toolName=play&status=failed', expect.any(AbortSignal));

    const rows = wrapper.findAll('.call-row');
    expect(rows[0]!.find('.row-outcome').exists()).toBe(false);
    expect(rows[1]!.get('.row-outcome').text()).toContain('Failed');
  });
  it('cancels superseded detail requests and ignores stale completion on route reuse and unmount', async () => {
    let finish!: (value: api.ToolCall) => void;
    let signal!: AbortSignal;
    vi.mocked(api.getToolCall).mockImplementationOnce((_id, value) => { signal = value!; return new Promise(done => { finish = done; }); });
    const { wrapper, router } = await open('/tool-calls/slow');
    await router.push('/tool-calls/second'); await flushPromises();
    expect(signal.aborted).toBe(true);
    finish(call('old_tool', 'slow')); await flushPromises();
    expect(wrapper.get('.call-heading').text()).toContain('search');
    expect(wrapper.text()).not.toContain('old_tool');
    vi.mocked(api.getToolCall).mockImplementationOnce((_id, value) => { signal = value!; return new Promise(done => { finish = done; }); });
    await router.push('/tool-calls/another'); await flushPromises();
    await router.push('/elsewhere');
    expect(signal.aborted).toBe(true);
    finish(call('old_tool', 'another')); await flushPromises();
    expect(wrapper.text()).toBe('Elsewhere');
  });
  it('retries an initial failed list without losing a valid selection', async () => {
    vi.mocked(api.listToolCalls).mockRejectedValueOnce(new Error('Temporarily unavailable.'));
    const { wrapper, router } = await open();
    expect(wrapper.text()).toContain('Temporarily unavailable.');
    await wrapper.get('.list-message button').trigger('click'); await flushPromises();
    expect(router.currentRoute.value.params.id).toBe('first');
    expect(api.listToolCalls).toHaveBeenCalledTimes(2);
    expect(wrapper.get('.call-heading h2').text()).toBe('search');
  });
  it('keeps text-only and raw diagnostic content accessible through keyboard tabs', async () => {
    const { wrapper } = await open();
    await wrapper.get('[role="tablist"]').trigger('keydown', { key: 'ArrowRight' });
    expect(wrapper.get('#call-tab-raw').attributes('aria-selected')).toBe('true');
    await wrapper.get('[role="tablist"]').trigger('keydown', { key: 'Home' });
    expect(wrapper.get('#call-tab-details').attributes('aria-selected')).toBe('true');
  });
  it('returns to the mobile list without discarding the selected URL or scroll position', async () => {
    const { wrapper, router } = await open('/tool-calls/second');
    expect(wrapper.get('.workspace').classes()).toContain('mobile-detail');
    await wrapper.get('.back-to-list').trigger('click'); await flushPromises();
    expect(router.currentRoute.value.params.id).toBe('second');
    expect(wrapper.get('.workspace').classes()).not.toContain('mobile-detail');
  });
});
