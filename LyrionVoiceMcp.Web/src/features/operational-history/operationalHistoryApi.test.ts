import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  getJob,
  listErrors,
  listJobs,
  listSchedules,
  listToolCalls,
  runSchedule,
  updateScheduleConfiguration
} from './operationalHistoryApi';

describe('operational history API', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses the paged list and detail routes', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ items: [], total: 0, offset: 0, limit: 50 }))
      .mockResolvedValueOnce(jsonResponse({ job: { id: 42 }, logs: [] }))
      .mockResolvedValueOnce(jsonResponse({ items: [], total: 0, offset: 0, limit: 50, retentionDays: 90 }))
      .mockResolvedValueOnce(jsonResponse({ items: [], total: 0, offset: 0, limit: 50, retentionDays: 30 }));
    vi.stubGlobal('fetch', fetchMock);

    await listJobs('?status=failed'); await getJob('42'); await listErrors(); await listToolCalls('?toolName=play');

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/jobs?status=failed', expect.any(Object));
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/jobs/42', expect.any(Object));
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/error-logs', expect.any(Object));
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/tool-calls?toolName=play', expect.any(Object));
  });

  it('exposes schedule listing and run-now', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse({ name: 'catalogue-change-check' }))
      .mockResolvedValueOnce(jsonResponse({ enqueuedCount: 1, jobIds: [7] }));
    vi.stubGlobal('fetch', fetchMock);

    await listSchedules();
    await updateScheduleConfiguration('catalogue-change-check', {
      enabled: true, intervalMinutes: 10, dailyTime: null
    });
    await runSchedule('catalogue-refresh');

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/scheduled-jobs', expect.any(Object));
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/scheduled-jobs/catalogue-change-check/configuration',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ enabled: true, intervalMinutes: 10, dailyTime: null })
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/scheduled-jobs/catalogue-refresh/run', expect.objectContaining({ method: 'POST' }));
  });

  it('surfaces validation details from schedule updates', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      json: vi.fn().mockResolvedValue({ errors: { intervalMinutes: ['Choose an interval.'] } })
    });
    vi.stubGlobal('fetch', fetchMock);

    await expect(updateScheduleConfiguration('catalogue-change-check', {
      enabled: true, intervalMinutes: 2, dailyTime: null
    })).rejects.toThrow('Choose an interval.');
  });
});

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, json: vi.fn().mockResolvedValue(body) } as unknown as Response;
}
