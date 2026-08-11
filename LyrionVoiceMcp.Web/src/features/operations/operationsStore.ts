import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import {
  getHealth,
  getLmsConnection,
  getVersion,
  type LmsConnectionResponse,
  type VersionResponse
} from './operationsApi';

export const useOperationsStore = defineStore('operations', () => {
  const loading = ref(false);
  const status = ref<string | null>(null);
  const version = ref<VersionResponse | null>(null);
  const lmsConnection = ref<LmsConnectionResponse | null>(null);
  const errorMessage = ref<string | null>(null);

  const isHealthy = computed(() => status.value === 'ok' && errorMessage.value === null);

  async function load(signal?: AbortSignal): Promise<void> {
    loading.value = true;
    errorMessage.value = null;

    try {
      const [healthResult, versionResult, lmsResult] = await Promise.all([
        getHealth(signal),
        getVersion(signal),
        getLmsConnection(signal)
      ]);
      status.value = healthResult.status;
      version.value = versionResult;
      lmsConnection.value = lmsResult;
    } catch (error) {
      status.value = null;
      version.value = null;
      lmsConnection.value = null;
      errorMessage.value = describeError(error);
    } finally {
      loading.value = false;
    }
  }

  return {
    loading,
    status,
    version,
    lmsConnection,
    errorMessage,
    isHealthy,
    load
  };
});

function describeError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The operational API could not be reached.';
}
