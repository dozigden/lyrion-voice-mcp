import { createPinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, nextTick } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../features/operations/operationsApi';
import { formatDate } from '../shared/format';
import AppLayout from './AppLayout.vue';

describe('AppLayout maintenance status', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime('2026-01-02T13:00:00Z');
    vi.spyOn(api, 'getCatalogue').mockResolvedValue(catalogueStatus());
    vi.spyOn(api, 'getSearchIndex').mockResolvedValue(indexStatus());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('renders successful build ages as exact semantic times and keeps them current', async () => {
    // Arrange
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/tool-calls', name: 'tool-calls', component: defineComponent({ template: '<main />' }) },
        { path: '/system', name: 'home', component: defineComponent({ template: '<main />' }) },
        { path: '/licences', name: 'licences', component: defineComponent({ template: '<main />' }) }
      ]
    });
    await router.push('/tool-calls');
    await router.isReady();

    // Act
    const wrapper = mount(AppLayout, { global: { plugins: [createPinia(), router] } });
    await flushPromises();

    // Assert
    const times = wrapper.findAll('.health time');
    expect(times.map(time => time.text())).toEqual(['12 min ago', '1 hr ago']);
    expect(times[0].attributes()).toMatchObject({
      datetime: '2026-01-02T12:48:00Z',
      title: formatDate('2026-01-02T12:48:00Z')
    });
    expect(wrapper.findAll('.health .dot.ready')).toHaveLength(2);

    vi.advanceTimersByTime(60_000);
    await nextTick();
    expect(wrapper.findAll('.health time').map(time => time.text())).toEqual(['13 min ago', '1 hr ago']);

    wrapper.unmount();
    expect(vi.getTimerCount()).toBe(0);
  });
});

function catalogueStatus(): api.CatalogueStatusResponse {
  return {
    summary: {
      sourceId: 'fictional', provider: 'test', sourceRevision: null, sourceVersion: null,
      capturedAt: '2026-01-02T12:48:00Z', sourceLastScanAt: null, refreshedAt: '2026-01-02T12:48:00Z',
      artistCount: 1, albumCount: 1, genreCount: 1, trackCount: 1, virtualLibraryCount: 0, warningCount: 0
    },
    latestRefresh: {
      id: 'refresh-1', status: 'succeeded', startedAt: '2026-01-02T12:47:00Z',
      completedAt: '2026-01-02T12:48:00Z', durationMilliseconds: 60_000, failureMessage: null, logs: []
    }
  };
}

function indexStatus(): api.SearchIndexStatusResponse {
  return {
    resolver: 'fictional',
    artifact: {
      resolverVersion: '1', catalogueRefreshId: 'refresh-1', builtAt: '2026-01-02T12:00:00Z',
      candidateCount: 1, preparationDurationMilliseconds: 1, indexSizeBytes: 1
    },
    latestJob: {
      id: 1, status: 'succeeded', startedAt: '2026-01-02T11:59:00Z',
      completedAt: '2026-01-02T12:00:00Z', errorMessage: null
    }
  };
}
