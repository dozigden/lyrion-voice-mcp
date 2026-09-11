import { afterEach, describe, expect, it, vi } from 'vitest';
import { getCatalogue, getSearchIndex, rebuildSearchIndex } from '../../features/operations/operationsApi';
import { getJob, getToolCall, listToolCalls, listJobs, listSchedules, runSchedule } from '../../features/operational-history/operationalHistoryApi';
import { call, summary } from '../../features/tool-log/toolLogFixtures';
afterEach(() => vi.unstubAllGlobals());
const respond = (value: unknown, status = 200) => vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(value), { status })));
describe('frontend API contract boundary', () => {
  it.each([
    [() => listJobs(), { items: [], total: 0, offset: 0, limit: 50 }],
    [() => getJob('7'), { job: { id: 7 }, logs: [] }],
    [() => listSchedules(), [{ name: 'refresh', editableConfiguration: { kind: 'interval', intervalMinutes: 'five' } }]],
    [() => getToolCall('fiction'), { ...call(), argumentsTruncated: 'false' }],
    [() => getCatalogue(), { summary: null, latestRefresh: { id: 'job', status: 'running', startedAt: '2026-01-01T00:00:00Z', completedAt: null, durationMilliseconds: null, failureMessage: null, logs: [{ id: 1, level: 'error', message: 42 }] } }],
    [() => getSearchIndex(), { resolver: 'fictional', artifact: { resolverVersion: '1', catalogueRefreshId: 'fiction', builtAt: '2026-01-01T00:00:00Z', candidateCount: 12, preparationDurationMilliseconds: 10, indexSizeBytes: 'large' }, latestJob: null }],
    [() => runSchedule('fiction'), { enqueuedCount: 1, jobIds: ['7'] }]
  ] as const)('rejects malformed consumed fields', async (load, value) => {
    respond(value);
    await expect(load()).rejects.toThrow('returned an invalid response');
  });
  it('accepts explicit nulls but rejects omitted required nullable fields and invalid timestamps', async () => {
    const record = call(); respond(record); expect(await getToolCall(record.id)).toEqual(record);
    const missing = { ...record, errorLogId: undefined }; respond(missing);
    await expect(getToolCall(record.id)).rejects.toThrow('invalid response');
    respond({ ...record, startedAt: 'not a date' });
    await expect(getToolCall(record.id)).rejects.toThrow('invalid response');
  });
  it.each(['The Lantern Hours', null])('accepts a nullable request summary in list responses', async requestSummary => {
    respond({ items: [{ ...summary('fiction'), requestSummary }], total: 1, offset: 0, limit: 50, retentionDays: 30 });
    expect((await listToolCalls()).items[0]!.requestSummary).toBe(requestSummary);
  });
  it.each([42, undefined])('rejects invalid or missing list summaries', async requestSummary => {
    respond({ items: [{ ...summary('fiction'), requestSummary }], total: 1, offset: 0, limit: 50, retentionDays: 30 });
    await expect(listToolCalls()).rejects.toThrow('invalid response');
  });
  it('uses the same backend message and HTTP fallback for reads and mutations', async () => {
    respond({ message: 'A rebuild is already pending.' }, 409);
    await expect(rebuildSearchIndex()).rejects.toThrow('A rebuild is already pending.');
    respond({ errors: { query: ['Invalid filter.'] } }, 400);
    await expect(listJobs()).rejects.toThrow('Invalid filter.');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('Unavailable', { status: 503 })));
    await expect(listJobs()).rejects.toThrow('/api/jobs returned HTTP 503.');
  });
});
