import { describe, expect, it } from 'vitest';
import { catalogueHeadline, indexHeadline } from './headlineStatus';
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
});
