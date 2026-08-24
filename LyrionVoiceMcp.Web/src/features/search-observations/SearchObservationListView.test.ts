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
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.get('h1').text()).toBe('Observation log');
    expect(wrapper.text()).toContain('Retained locally for 30 days.');
    expect(wrapper.text()).not.toContain('Search evidence');
    expect(wrapper.text()).not.toContain('Inspect how each resolver searched');
    expect(wrapper.text()).not.toContain('Search terms and library metadata may be sensitive');
  });
});
