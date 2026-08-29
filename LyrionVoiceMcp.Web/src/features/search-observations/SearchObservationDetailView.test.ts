import { createPinia, setActivePinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './searchObservationsApi';
import SearchObservationDetailView from './SearchObservationDetailView.vue';

describe('SearchObservationDetailView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  it('distinguishes a failed request from a genuine no-match case', async () => {
    // Arrange
    vi.spyOn(api, 'getSearchObservation').mockResolvedValue({
      id: 'failed-search',
      createdAt: '2026-08-12T18:00:00Z',
      originalQuery: '',
      normalisedQuery: '',
      interpretation: 'broad_discovery',
      rating: null,
      ratingMatch: null,
      genre: null,
      requestedFromYear: null,
      requestedToYear: null,
      effectiveFromYear: null,
      effectiveToYear: null,
      requestedKind: null,
      provider: 'lms',
      collection: 'whole_library',
      resolver: 'lms-pass-through',
      resolverVersion: '1',
      status: 'failed',
      failureMessage: 'LMS search failed for library.',
      totalDurationMilliseconds: 12,
      retrievalDurationMilliseconds: 10,
      processingDurationMilliseconds: 2,
      requests: [{
        source: 'library',
        command: '["search"]',
        status: 'failed',
        failureMessage: 'Synthetic failure.',
        durationMilliseconds: 10,
        resultCount: 0
      }],
      candidates: [],
      review: null,
      retentionDays: 90
    });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/search-observations', name: 'search-observations', component: { template: '<div />' } },
        { path: '/search-observations/:id', name: 'detail', component: SearchObservationDetailView }
      ]
    });
    await router.push('/search-observations/failed-search');
    await router.isReady();

    // Act
    const wrapper = mount(SearchObservationDetailView, {
      global: { plugins: [createPinia(), router] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.get('h1').text()).toBe('Broad discovery');
    expect(wrapper.text()).toContain('Search request failed');
    expect(wrapper.text()).toContain('No candidates were recovered before the request failed.');
    expect(wrapper.text()).not.toContain('Search returned no candidates.');
    expect(wrapper.get('input[type="checkbox"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('select').element.value).toBe('other');
  });

  it('labels a resolved exact artist candidate', async () => {
    // Arrange
    vi.spyOn(api, 'getSearchObservation').mockResolvedValue({
      id: 'exact-artist-search',
      createdAt: '2026-08-23T15:00:00Z',
      originalQuery: 'The Copper Lines',
      normalisedQuery: 'The Copper Lines',
      interpretation: 'named',
      rating: null,
      ratingMatch: null,
      genre: null,
      requestedFromYear: null,
      requestedToYear: null,
      effectiveFromYear: null,
      effectiveToYear: null,
      requestedKind: null,
      provider: 'catalogue+lms',
      collection: 'whole_library',
      resolver: 'catalogue-phuzzy-sqlite',
      resolverVersion: '4',
      status: 'completed',
      failureMessage: null,
      totalDurationMilliseconds: 12,
      retrievalDurationMilliseconds: 10,
      processingDurationMilliseconds: 2,
      requests: [],
      candidates: [{
        position: 1,
        correlationId: 'exact-artist-correlation',
        kind: 'artist',
        title: 'The Copper Lines',
        artist: null,
        album: null,
        rating: null,
        selectedAt: null,
        isExactArtistMatch: true,
        matchSignal: 'exact_normalised'
      }],
      review: null,
      retentionDays: 90
    });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/search-observations', name: 'search-observations', component: { template: '<div />' } },
        { path: '/search-observations/:id', name: 'detail', component: SearchObservationDetailView }
      ]
    });
    await router.push('/search-observations/exact-artist-search');
    await router.isReady();

    // Act
    const wrapper = mount(SearchObservationDetailView, {
      global: { plugins: [createPinia(), router] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('The Copper Lines');
    expect(wrapper.text()).toContain('exact artist match');
    expect(wrapper.text()).toContain('exact normalised');
  });
});
