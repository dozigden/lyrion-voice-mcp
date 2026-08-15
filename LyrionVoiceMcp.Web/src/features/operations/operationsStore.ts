import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import {
  getCatalogue,
  getHealth,
  getLmsConnection,
  getVersion,
  rebuildCatalogue,
  type CatalogueStatusResponse,
  type LmsConnectionResponse,
  type VersionResponse
} from './operationsApi';

export const useOperationsStore = defineStore('operations', () => {
  const loading = ref(false);
  const status = ref<string | null>(null);
  const version = ref<VersionResponse | null>(null);
  const lmsConnection = ref<LmsConnectionResponse | null>(null);
  const errorMessage = ref<string | null>(null);
  const catalogue = ref<CatalogueStatusResponse | null>(null);
  const catalogueLoading = ref(false);
  const catalogueRebuildPending = ref(false);
  const catalogueErrorMessage = ref<string | null>(null);

  const isHealthy = computed(() => status.value === 'ok' && errorMessage.value === null);
  const catalogueRebuilding = computed(
    () => catalogue.value?.latestRefresh?.status === 'running');

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

  async function loadCatalogue(signal?: AbortSignal): Promise<void> {
    catalogueLoading.value = true;
    catalogueErrorMessage.value = null;

    try {
      catalogue.value = await getCatalogue(signal);
    } catch (error) {
      catalogueErrorMessage.value = describeCatalogueError(error);
    } finally {
      catalogueLoading.value = false;
    }
  }

  async function rebuild(signal?: AbortSignal): Promise<void> {
    catalogueRebuildPending.value = true;
    catalogueErrorMessage.value = null;

    try {
      catalogue.value = await rebuildCatalogue(signal);
    } catch (error) {
      catalogueErrorMessage.value = describeCatalogueError(error);
    } finally {
      catalogueRebuildPending.value = false;
    }
  }

  return {
    loading,
    status,
    version,
    lmsConnection,
    errorMessage,
    catalogue,
    catalogueLoading,
    catalogueRebuildPending,
    catalogueErrorMessage,
    isHealthy,
    catalogueRebuilding,
    load,
    loadCatalogue,
    rebuild
  };
});

function describeError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The operational API could not be reached.';
}

function describeCatalogueError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The catalogue API could not be reached.';
}
