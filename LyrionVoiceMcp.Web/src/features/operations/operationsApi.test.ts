import { afterEach, describe, expect, it, vi } from 'vitest';
import { getCatalogue, getHealth, getLmsConnection, rebuildCatalogue } from './operationsApi';

describe('operationsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the health payload', async () => {
    // Arrange
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ status: 'ok' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } }
    ));
    vi.stubGlobal('fetch', fetchMock);

    // Act
    const result = await getHealth();

    // Assert
    expect(result).toEqual({ status: 'ok' });
    expect(fetchMock).toHaveBeenCalledWith('/api/health', expect.objectContaining({
      headers: { Accept: 'application/json' }
    }));
  });

  it('reports an unsuccessful response', async () => {
    // Arrange
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })));

    // Act
    const result = getHealth();

    // Assert
    await expect(result).rejects.toThrow('/api/health returned HTTP 503.');
  });

  it('reports a malformed response', async () => {
    // Arrange
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ state: 'fine' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } }
    )));

    // Act
    const result = getHealth();

    // Assert
    await expect(result).rejects.toThrow('/api/health returned an invalid response.');
  });

  it('returns LMS connection details', async () => {
    // Arrange
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        status: 'online',
        serverId: 'development',
        baseUrl: 'http://music.test:9000',
        serverVersion: '9.0.1',
        message: 'Connected.'
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } }
    )));

    // Act
    const result = await getLmsConnection();

    // Assert
    expect(result.status).toBe('online');
    expect(result.serverId).toBe('development');
    expect(result.serverVersion).toBe('9.0.1');
  });

  it('returns catalogue status', async () => {
    // Arrange
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(catalogueStatus('succeeded'), 200));
    vi.stubGlobal('fetch', fetchMock);

    // Act
    const result = await getCatalogue();

    // Assert
    expect(result.summary?.trackCount).toBe(12_345);
    expect(result.latestRefresh?.status).toBe('succeeded');
    expect(fetchMock).toHaveBeenCalledWith('/api/catalogue', expect.objectContaining({
      headers: { Accept: 'application/json' }
    }));
  });

  it('starts a catalogue rebuild', async () => {
    // Arrange
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(catalogueStatus('running'), 202));
    vi.stubGlobal('fetch', fetchMock);

    // Act
    const result = await rebuildCatalogue();

    // Assert
    expect(result.latestRefresh?.status).toBe('running');
    expect(fetchMock).toHaveBeenCalledWith('/api/catalogue/refresh', expect.objectContaining({
      method: 'POST',
      headers: { Accept: 'application/json' }
    }));
  });
});

function catalogueStatus(status: 'running' | 'succeeded'): Record<string, unknown> {
  return {
    summary: {
      sourceId: 'development',
      provider: 'lms',
      sourceRevision: '1786379003',
      sourceVersion: '9.1.2',
      capturedAt: '2026-08-15T12:00:00Z',
      sourceLastScanAt: '2026-08-15T11:30:00Z',
      refreshedAt: '2026-08-15T12:00:01Z',
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

function jsonResponse(value: unknown, status: number): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}
