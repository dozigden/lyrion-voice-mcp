import { createPinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import * as api from './searchObservationsApi';
import SearchObservationListView from './SearchObservationListView.vue';

describe('SearchObservationListView', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows a concise heading and retention notice', async () => {
    // Arrange
    vi.spyOn(api, 'browseSearchObservations').mockResolvedValue({
      items: [],
      total: 0,
      offset: 0,
      limit: 50,
      retentionDays: 30
    });

    // Act
    const wrapper = mount(SearchObservationListView, {
      global: {
        plugins: [createPinia()],
        stubs: { RouterLink: { template: '<a><slot /></a>' } }
      }
    });
    await flushPromises();

    // Assert
    expect(wrapper.get('h1').text()).toBe('Observation log');
    expect(wrapper.text()).toContain('Retained locally for 30 days.');
    expect(wrapper.text()).not.toContain('Search evidence');
    expect(wrapper.text()).not.toContain('Inspect how each resolver searched');
    expect(wrapper.text()).not.toContain('Search terms and library metadata may be sensitive');
  });

  it('labels broad, name-free filtered, and historical observations', async () => {
    // Arrange
    vi.spyOn(api, 'browseSearchObservations').mockResolvedValue({
      items: [
        summary('broad', '   ', 'broad_discovery'),
        summary('filtered', '', 'name_free_filtered'),
        summary('historical', '', null)
      ],
      total: 3,
      offset: 0,
      limit: 50,
      retentionDays: 30
    });

    // Act
    const wrapper = mount(SearchObservationListView, {
      global: {
        plugins: [createPinia()],
        stubs: { RouterLink: { template: '<a><slot /></a>' } }
      }
    });
    await flushPromises();

    // Assert
    expect(wrapper.findAll('.query').map(item => item.text())).toEqual([
      'Broad discovery',
      'Name-free filtered search',
      'Constraint-only search'
    ]);
  });

  function summary(
    id: string,
    originalQuery: string,
    interpretation: api.SearchInterpretation | null
  ): api.SearchObservationSummary {
    return {
      id,
      createdAt: '2026-08-26T08:00:00Z',
      originalQuery,
      interpretation,
      resolver: 'fictional-resolver',
      resolverVersion: '1',
      status: 'completed',
      resultCount: 2,
      selectedPosition: null,
      totalDurationMilliseconds: 10,
      classification: null,
      includeInEvaluation: false
    };
  }
});
