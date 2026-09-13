import { describe, expect, it } from 'vitest';
import { catalogueHeaderStatus, catalogueHeadline, indexHeaderStatus, indexHeadline } from './headlineStatus';
import type { CatalogueStatusResponse, SearchIndexStatusResponse } from './operationsApi';
describe('compact maintenance status', () => {
  it('distinguishes an available artifact with a failed rebuild from absence', () => {
    const index: SearchIndexStatusResponse = { resolver: 'fictional', artifact: { resolverVersion: '1', catalogueRefreshId: 'fictional', builtAt: '2026-01-01T00:00:00Z', candidateCount: 1, preparationDurationMilliseconds: 1, indexSizeBytes: 1 }, latestJob: { id: 1, status: 'failed', startedAt: null, completedAt: null, errorMessage: 'Failed' } };
    expect(indexHeadline(index, false, null)).toBe('Ready · attention');
    expect(indexHeadline({ ...index, artifact: null }, false, null)).toBe('Attention');
  });
  it('shows unknown, checking, missing and unreachable states without inventing progress', () => {
    const catalogue: CatalogueStatusResponse = { summary: null, latestRefresh: null };
    expect(catalogueHeadline(null, false, null)).toBe('Unknown');
    expect(catalogueHeadline(null, true, null)).toBe('Checking');
    expect(catalogueHeadline(catalogue, false, null)).toBe('Not built');
    expect(catalogueHeadline(catalogue, false, 'Unavailable')).toBe('Unavailable');
  });

  it('shows the age of the successful catalogue and index artifacts in the header', () => {
    // Arrange
    const now = new Date('2026-01-02T13:00:00Z').getTime();
    const catalogue = catalogueStatus('2026-01-02T12:48:00Z');
    const index = indexStatus('2026-01-02T11:00:00Z', 'succeeded');

    // Act
    const catalogueHeader = catalogueHeaderStatus(catalogue, false, null, now);
    const searchIndexStatus = indexHeaderStatus(index, false, null, now);

    // Assert
    expect(catalogueHeader).toEqual({ label: '12 min ago', timestamp: '2026-01-02T12:48:00Z', ready: true });
    expect(searchIndexStatus).toEqual({ label: '2 hr ago', timestamp: '2026-01-02T11:00:00Z', ready: true });
  });

  it('keeps the successful artifact age visible when a later rebuild needs attention', () => {
    // Arrange
    const now = new Date('2026-01-03T13:00:00Z').getTime();
    const index = indexStatus('2026-01-02T11:00:00Z', 'failed');

    // Act
    const status = indexHeaderStatus(index, false, null, now);

    // Assert
    expect(status).toEqual({ label: '1 day ago · attention', timestamp: '2026-01-02T11:00:00Z', ready: false });
  });

  it('preserves non-ready states without presenting an artifact timestamp', () => {
    const catalogue = catalogueStatus('2026-01-02T12:48:00Z');
    catalogue.latestRefresh = { ...catalogue.latestRefresh!, status: 'running' };

    expect(catalogueHeaderStatus(catalogue, false, null)).toEqual({ label: 'Rebuilding', timestamp: null, ready: false });
    expect(catalogueHeaderStatus(catalogue, false, 'Unavailable')).toEqual({ label: 'Unavailable', timestamp: null, ready: false });
  });

});

function catalogueStatus(refreshedAt: string): CatalogueStatusResponse {
  return {
    summary: {
      sourceId: 'fictional', provider: 'test', sourceRevision: null, sourceVersion: null,
      capturedAt: refreshedAt, sourceLastScanAt: null, refreshedAt, artistCount: 1, albumCount: 1,
      genreCount: 1, trackCount: 1, virtualLibraryCount: 0, warningCount: 0
    },
    latestRefresh: {
      id: 'refresh-1', status: 'succeeded', startedAt: refreshedAt, completedAt: refreshedAt,
      durationMilliseconds: 1, failureMessage: null, logs: []
    }
  };
}

function indexStatus(builtAt: string, status: 'succeeded' | 'failed'): SearchIndexStatusResponse {
  return {
    resolver: 'fictional',
    artifact: {
      resolverVersion: '1', catalogueRefreshId: 'refresh-1', builtAt, candidateCount: 1,
      preparationDurationMilliseconds: 1, indexSizeBytes: 1
    },
    latestJob: {
      id: 1, status, startedAt: builtAt, completedAt: builtAt,
      errorMessage: status === 'failed' ? 'Fictional failure.' : null
    }
  };
}
