import { describe, expect, it } from 'vitest';
import { remoteResource } from './remoteResource';
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
describe('remote request lifecycle', () => {
  it('aborts superseded work and ignores a late response even when its client ignores abort', async () => {
    const state = remoteResource<string>(), first = deferred<string>(), second = deferred<string>();
    let signal!: AbortSignal;
    const older = state.load(value => { signal = value; return first.promise; });
    const newer = state.load(() => second.promise);
    expect(signal.aborted).toBe(true);
    first.resolve('old'); await older;
    expect(state.loading.value).toBe(true);
    expect(state.data.value).toBeNull();
    second.resolve('new'); await newer;
    expect(state.data.value).toBe('new');
    expect(state.loading.value).toBe(false);
  });
  it('suppresses errors and writes after owner cancellation', async () => {
    const state = remoteResource<string>(), response = deferred<string>();
    const task = state.load(() => response.promise);
    state.cancel(); response.resolve('after unmount'); await task;
    expect(state.data.value).toBeNull(); expect(state.error.value).toBeNull(); expect(state.loading.value).toBe(false);
  });
  it('retains explicitly preserved data on a failed refresh and recovers on retry', async () => {
    const state = remoteResource<string>();
    await state.load(async () => 'ready');
    await state.load(async () => { throw new Error('Unavailable'); }, true);
    expect(state.data.value).toBe('ready'); expect(state.error.value).toBe('Unavailable');
    await state.load(async () => 'refreshed', true);
    expect(state.error.value).toBeNull(); expect(state.data.value).toBe('refreshed');
  });
});
