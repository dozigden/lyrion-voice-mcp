import { createPinia, setActivePinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './operationsApi';
import OperationalHomeView from './OperationalHomeView.vue';

describe('OperationalHomeView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
    vi.spyOn(api, 'getCatalogue').mockResolvedValue({
      summary: null,
      latestRefresh: null
    });
    vi.spyOn(api, 'getSearchIndex').mockResolvedValue(searchIndexStatus('succeeded'));
  });

  it('shows connection details and the release version without the removed cards', async () => {
    // Arrange
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'ui-test',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'online',
      serverId: 'development',
      baseUrl: 'http://music.test:9000',
      serverVersion: '9.0.1',
      message: 'Connected.'
    });

    // Act
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('Online');
    expect(wrapper.text()).toContain('0.1.0');
    expect(wrapper.text()).toContain(new URL('/mcp', window.location.origin).href);
    expect(wrapper.text()).toContain('development');
    expect(wrapper.text()).toContain('LMS 9.0.1');
    expect(wrapper.text()).toContain('Trusted LAN only');
    expect(wrapper.findAll('.operation-row button').map(button => button.text())).toEqual([
      'Rebuild',
      'Rebuild'
    ]);
    expect(wrapper.text()).not.toContain('A voice-oriented bridge');
    expect(wrapper.text()).not.toContain('Streamable HTTP');
    expect(wrapper.text()).not.toContain('Channel');
    expect(wrapper.find('.hero__icon').exists()).toBe(true);
    expect(wrapper.findAll('.operation-row')).toHaveLength(2);
  });

  it('shows an unavailable operational API', async () => {
    // Arrange
    vi.spyOn(api, 'getVersion').mockRejectedValue(new Error('API unavailable.'));
    vi.spyOn(api, 'getLmsConnection').mockRejectedValue(new Error('API unavailable.'));

    // Act
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('API unavailable.');
    expect(wrapper.text()).toContain('Version unavailable');
  });

  it('shows catalogue contents and starts a rebuild', async () => {
    // Arrange
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'ui-test',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'online',
      serverId: 'development',
      baseUrl: 'http://music.test:9000',
      serverVersion: '9.0.1',
      message: 'Connected.'
    });
    vi.mocked(api.getCatalogue).mockResolvedValue(catalogueStatus('succeeded'));
    vi.spyOn(api, 'rebuildCatalogue').mockResolvedValue(catalogueStatus('running'));
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Act
    await wrapper.get('.catalogue-rebuild').trigger('click');
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('12,345');
    expect(wrapper.get('.catalogue-rebuild').text()).toBe('Rebuild');
    expect(api.rebuildCatalogue).toHaveBeenCalledOnce();
    wrapper.unmount();
  });

  it('shows the production search index and starts its rebuild', async () => {
    // Arrange
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'ui-test',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'online',
      serverId: 'development',
      baseUrl: 'http://music.test:9000',
      serverVersion: '9.0.1',
      message: 'Connected.'
    });
    vi.mocked(api.getCatalogue).mockResolvedValue(catalogueStatus('succeeded'));
    vi.mocked(api.getSearchIndex).mockResolvedValue(searchIndexStatus('succeeded'));
    vi.spyOn(api, 'rebuildSearchIndex').mockResolvedValue(searchIndexStatus('pending'));
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Act
    await wrapper.get('.index-rebuild').trigger('click');
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('catalogue-phuzzy-sqlite');
    expect(wrapper.text()).toContain('1,234 candidates');
    expect(wrapper.text()).toContain('Job 42 · pending');
    expect(api.rebuildSearchIndex).toHaveBeenCalledWith(undefined);
    wrapper.unmount();
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

function catalogueStatus(status: 'running' | 'succeeded'): api.CatalogueStatusResponse {
  return {
    summary: {
      sourceId: 'development',
      provider: 'lms',
      sourceRevision: '1786379003',
      sourceVersion: '9.1.2',
      capturedAt: '2026-08-15T12:00:00Z',
      sourceLastScanAt: '2026-08-15T11:30:00Z',
      refreshedAt: '2026-08-15T12:02:38Z',
      artistCount: 1_234,
      albumCount: 2_345,
      genreCount: 67,
      trackCount: 12_345,
      virtualLibraryCount: 4,
      warningCount: 0
    },
    latestRefresh: {
      id: 'refresh-1',
      status,
      startedAt: '2026-08-15T12:00:00Z',
      completedAt: status === 'running' ? null : '2026-08-15T12:02:38Z',
      durationMilliseconds: status === 'running' ? null : 158_000,
      failureMessage: null,
      logs: [{
        id: 1,
        occurredAt: '2026-08-15T12:00:01Z',
        level: 'information',
        message: 'Started reading the LMS catalogue.',
        processedCount: null,
        totalCount: null
      }]
    }
  };
}
