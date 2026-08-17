import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './operationsApi';
import { useOperationsStore } from './operationsStore';

describe('operationsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  it('loads healthy runtime details', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' });
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'local',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'online',
      serverId: 'development',
      baseUrl: 'http://music.test:9000',
      serverVersion: '9.0.1',
      message: 'Connected.'
    });
    const store = useOperationsStore();

    // Act
    await store.load();

    // Assert
    expect(store.isHealthy).toBe(true);
    expect(store.version?.commit).toBe('abcdef0');
    expect(store.lmsConnection?.status).toBe('online');
    expect(store.errorMessage).toBeNull();
  });

  it('clears stale data when the API fails', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockRejectedValue(new Error('Network unavailable.'));
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'local',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'not_configured',
      serverId: null,
      baseUrl: null,
      serverVersion: null,
      message: 'Not configured.'
    });
    const store = useOperationsStore();

    // Act
    await store.load();

    // Assert
    expect(store.isHealthy).toBe(false);
    expect(store.version).toBeNull();
    expect(store.lmsConnection).toBeNull();
    expect(store.errorMessage).toBe('Network unavailable.');
  });

  it('loads and rebuilds the catalogue independently of runtime status', async () => {
    // Arrange
    vi.spyOn(api, 'getCatalogue').mockResolvedValue({
      summary: null,
      latestRefresh: null
    });
    vi.spyOn(api, 'rebuildCatalogue').mockResolvedValue({
      summary: null,
      latestRefresh: {
        id: 'refresh-1',
        status: 'running',
        startedAt: '2026-08-15T12:00:00Z',
        completedAt: null,
        durationMilliseconds: null,
        failureMessage: null,
        logs: []
      }
    });
    const store = useOperationsStore();

    // Act
    await store.loadCatalogue();
    await store.rebuild();

    // Assert
    expect(store.catalogueRebuilding).toBe(true);
    expect(store.catalogueErrorMessage).toBeNull();
    expect(api.rebuildCatalogue).toHaveBeenCalledOnce();
  });

  it('retains catalogue status when polling fails', async () => {
    // Arrange
    vi.spyOn(api, 'getCatalogue')
      .mockResolvedValueOnce({ summary: null, latestRefresh: null })
      .mockRejectedValueOnce(new Error('Catalogue unavailable.'));
    const store = useOperationsStore();
    await store.loadCatalogue();

    // Act
    await store.loadCatalogue();

    // Assert
    expect(store.catalogue).toEqual({ summary: null, latestRefresh: null });
    expect(store.catalogueErrorMessage).toBe('Catalogue unavailable.');
  });

  it('loads the production search index and retains its artifact while a rebuild starts', async () => {
    // Arrange
    const published = searchIndexStatus('succeeded');
    vi.spyOn(api, 'getSearchIndex').mockResolvedValue(published);
    vi.spyOn(api, 'rebuildSearchIndex').mockResolvedValue(searchIndexStatus('pending'));
    const store = useOperationsStore();

    // Act
    await store.loadSearchIndexes();
    await store.rebuildIndex();

    // Assert
    expect(store.searchIndex?.artifact).toEqual(published.artifact);
    expect(store.searchIndexesRebuilding).toBe(true);
    expect(store.searchIndexesErrorMessage).toBeNull();
  });
});

function searchIndexStatus(
  status: 'pending' | 'succeeded'
): api.SearchIndexStatusResponse {
  return {
    resolver: 'catalogue-phuzzy-sqlite',
    artifact: {
      resolverVersion: '2',
      catalogueRefreshId: 'refresh-1',
      builtAt: '2026-08-15T12:03:00Z',
      candidateCount: 1_234,
      preparationDurationMilliseconds: 920,
      indexSizeBytes: 65_536
    },
    latestJob: {
      id: 42,
      status,
      startedAt: status === 'pending' ? null : '2026-08-15T12:02:40Z',
      completedAt: status === 'pending' ? null : '2026-08-15T12:03:00Z',
      errorMessage: null
    }
  };
}
