import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './operationalHistoryApi';
import { useHistoryStore } from './historyStore';
import { useSchedulesStore } from './schedulesStore';
beforeEach(() => { setActivePinia(createPinia()); vi.restoreAllMocks(); });
describe('history request ownership', () => {
  it('does not reopen an old job when cancellation finishes after navigation', async () => {
    let finish!: (value: api.Job) => void;
    let signal!: AbortSignal;
    vi.spyOn(api, 'cancelJob').mockImplementation((_id, value) => { signal = value!; return new Promise(done => { finish = done; }); });
    const error: api.ErrorLog = { id: 2, reportId: null, occurredAt: '2026-01-01T00:00:00Z', source: 'test', area: 'test', exceptionType: 'TestError', message: 'Fictional error', stackTrace: null, traceIdentifier: null, requestMethod: null, requestPath: null, jobId: null, contextJson: null, createdAt: '2026-01-01T00:00:00Z' };
    vi.spyOn(api, 'getError').mockResolvedValue(error);
    const jobLoad = vi.spyOn(api, 'getJob');
    const store = useHistoryStore();
    const cancellation = store.cancelJob(1);
    await store.loadDetail('errors', '2');
    expect(signal.aborted).toBe(true);
    finish({ id: 1, type: 'fictional', status: 'cancelled', runAfter: '2026-01-01T00:00:00Z', payloadJson: '{}', resultJson: '{}', errorMessage: null, startedAt: null, completedAt: null, correlationId: null, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' });
    await cancellation;
    expect(store.record).toEqual(error);
    expect(jobLoad).not.toHaveBeenCalled();
  });
  it('aborts a pending schedule mutation and prevents its subsequent refresh after unmount', async () => {
    let finish!: (value: { enqueuedCount: number; jobIds: number[] }) => void;
    let signal!: AbortSignal;
    vi.spyOn(api, 'runSchedule').mockImplementation((_name, value) => { signal = value!; return new Promise(done => { finish = done; }); });
    const reload = vi.spyOn(api, 'listSchedules');
    const store = useSchedulesStore();
    const task = store.run('fictional');
    expect(store.pending.fictional).toBe('running');
    store.cancel();
    expect(signal.aborted).toBe(true);
    finish({ enqueuedCount: 1, jobIds: [1] }); await task;
    expect(reload).not.toHaveBeenCalled();
    expect(store.pending).toEqual({});
    expect(store.error).toBeNull();
  });
});
