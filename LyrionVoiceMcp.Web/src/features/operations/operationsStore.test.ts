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
});
