import { ref, shallowRef } from 'vue';

/** One owner per request stream. Abort is paired with identity checks for clients that finish after cancellation. */
export function remoteResource<T>() {
  const data = shallowRef<T | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  let active: AbortController | null = null;
  function cancel() {
    active?.abort();
    active = null;
    loading.value = false;
  }
  async function load(fetcher: (signal: AbortSignal) => Promise<T>, preserve = false, signal?: AbortSignal): Promise<T | null> {
    cancel();
    const controller = new AbortController();
    active = controller;
    const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) controller.abort();
    loading.value = true;
    error.value = null;
    if (!preserve) data.value = null;
    try {
      const result = await fetcher(controller.signal);
      if (active !== controller || controller.signal.aborted) return null;
      data.value = result;
      return result;
    } catch (reason) {
      if (active === controller && !controller.signal.aborted) {
        error.value = reason instanceof Error ? reason.message : 'The request could not be completed.';
      }
      return null;
    } finally {
      signal?.removeEventListener('abort', abort);
      if (active === controller) { active = null; loading.value = false; }
    }
  }
  return { data, loading, error, load, cancel };
}
