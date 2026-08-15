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
  });

  it('shows a healthy build', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' });
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
    expect(wrapper.text()).toContain('/mcp');
    expect(wrapper.text()).toContain('development');
    expect(wrapper.text()).toContain('LMS 9.0.1');
    expect(wrapper.text()).toContain('Trusted network only');
    expect(wrapper.text()).toContain('Rebuild catalogue');
  });

  it('shows an unavailable service', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockRejectedValue(new Error('API unavailable.'));
    vi.spyOn(api, 'getVersion').mockRejectedValue(new Error('API unavailable.'));
    vi.spyOn(api, 'getLmsConnection').mockRejectedValue(new Error('API unavailable.'));

    // Act
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('Unavailable');
    expect(wrapper.text()).toContain('API unavailable.');
  });

  it('shows catalogue contents and starts a rebuild', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' });
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
    expect(wrapper.text()).toContain('Rebuild in progress…');
    expect(api.rebuildCatalogue).toHaveBeenCalledOnce();
    wrapper.unmount();
  });
});

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
