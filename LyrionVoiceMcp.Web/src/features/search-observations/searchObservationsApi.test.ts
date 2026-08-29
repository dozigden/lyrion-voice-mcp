import { afterEach, describe, expect, it, vi } from 'vitest';
import { browseSearchObservations, getSearchObservation, saveSearchReview } from './searchObservationsApi';

describe('search observations API', () => {
  afterEach(() => vi.restoreAllMocks());

  it('sends browse filters and validates the response', async () => {
    // Arrange
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{
        id: 'one', createdAt: '2026-08-12T18:00:00Z', originalQuery: 'zyrack',
        interpretation: 'named',
        resolver: 'lms-pass-through', resolverVersion: '1', status: 'completed', resultCount: 0,
        selectedPosition: null, totalDurationMilliseconds: 12, classification: null, includeInEvaluation: false
      }],
      total: 1, offset: 0, limit: 50, retentionDays: 90
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    // Act
    const page = await browseSearchObservations({ query: ' zyrack ', review: 'unreviewed', result: 'no-results' });

    // Assert
    expect(page.items[0]?.originalQuery).toBe('zyrack');
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain('query=zyrack');
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain('result=no-results');
  });

  it('uses PUT when saving a review', async () => {
    // Arrange
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      id: 'one', createdAt: '2026-08-12T18:00:00Z', originalQuery: 'zyrack', normalisedQuery: 'zyrack',
      interpretation: 'named',
      rating: null, ratingMatch: null, genre: null, requestedFromYear: null, requestedToYear: null,
      effectiveFromYear: null, effectiveToYear: null, requestedKind: null, provider: 'lms', collection: 'whole_library', resolver: 'lms-pass-through',
      resolverVersion: '1', status: 'completed', failureMessage: null, totalDurationMilliseconds: 12,
      retrievalDurationMilliseconds: 10, processingDurationMilliseconds: 2, requests: [], candidates: [],
      review: null, retentionDays: 90
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    // Act
    await saveSearchReview('one', {
      classification: 'no_match', expectedCorrelationId: null, expectedKind: 'artist',
      expectedTitle: 'ZYRAQ', expectedArtist: null, expectedAlbum: null, notes: null, includeInEvaluation: true
    });

    // Assert
    expect(fetch).toHaveBeenCalledWith('/api/search-observations/one/review', expect.objectContaining({ method: 'PUT' }));
  });

  it('requires the exact artist interpretation marker on candidates', async () => {
    // Arrange
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      id: 'one', createdAt: '2026-08-23T15:00:00Z', originalQuery: 'The Copper Lines', normalisedQuery: 'The Copper Lines',
      interpretation: 'named',
      rating: null, ratingMatch: null, genre: null, requestedFromYear: null, requestedToYear: null,
      effectiveFromYear: null, effectiveToYear: null, requestedKind: null, provider: 'catalogue+lms', collection: 'whole_library',
      resolver: 'catalogue-phuzzy-sqlite', resolverVersion: '4', status: 'completed', failureMessage: null,
      totalDurationMilliseconds: 12, retrievalDurationMilliseconds: 10, processingDurationMilliseconds: 2, requests: [],
      candidates: [{
        position: 1, correlationId: 'exact-artist-correlation', kind: 'artist', title: 'The Copper Lines',
        artist: null, album: null, rating: null, selectedAt: null, isExactArtistMatch: true,
        matchSignal: 'exact_normalised'
      }],
      review: null, retentionDays: 90
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    // Act
    const detail = await getSearchObservation('one');

    // Assert
    expect(detail.interpretation).toBe('named');
    expect(detail.candidates[0]?.isExactArtistMatch).toBe(true);
    expect(detail.candidates[0]?.matchSignal).toBe('exact_normalised');
  });
});
