import { afterEach, describe, expect, it, vi } from 'vitest';
import { getHealth, getLmsConnection } from './operationsApi';

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
});
