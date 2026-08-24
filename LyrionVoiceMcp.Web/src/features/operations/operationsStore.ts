import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import {
  getCatalogue,
  getLmsConnection,
  getSearchIndex,
  getVersion,
  rebuildCatalogue,
  rebuildSearchIndex,
  type CatalogueStatusResponse,
  type LmsConnectionResponse,
  type SearchIndexStatusResponse,
  type VersionResponse
} from './operationsApi';

export const useOperationsStore = defineStore('operations', () => {
  const loading = ref(false);
  const version = ref<VersionResponse | null>(null);
  const lmsConnection = ref<LmsConnectionResponse | null>(null);
  const errorMessage = ref<string | null>(null);
  const catalogue = ref<CatalogueStatusResponse | null>(null);
  const catalogueLoading = ref(false);
  const catalogueRebuildPending = ref(false);
  const catalogueErrorMessage = ref<string | null>(null);
  const searchIndex = ref<SearchIndexStatusResponse | null>(null);
  const searchIndexesLoading = ref(false);
  const searchIndexRebuildPending = ref(false);
  const searchIndexesErrorMessage = ref<string | null>(null);

  const catalogueRebuilding = computed(
    () => catalogue.value?.latestRefresh?.status === 'running');
  const searchIndexesRebuilding = computed(() =>
    searchIndex.value?.latestJob?.status === 'pending'
    || searchIndex.value?.latestJob?.status === 'running');

  async function load(signal?: AbortSignal): Promise<void> {
    loading.value = true;
    errorMessage.value = null;

    try {
      const [versionResult, lmsResult] = await Promise.all([
        getVersion(signal),
        getLmsConnection(signal)
      ]);
      version.value = versionResult;
      lmsConnection.value = lmsResult;
    } catch (error) {
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

  async function loadSearchIndexes(signal?: AbortSignal): Promise<void> {
    searchIndexesLoading.value = true;
    searchIndexesErrorMessage.value = null;

    try {
      searchIndex.value = await getSearchIndex(signal);
    } catch (error) {
      searchIndexesErrorMessage.value = describeSearchIndexError(error);
    } finally {
      searchIndexesLoading.value = false;
    }
  }

  async function rebuildIndex(signal?: AbortSignal): Promise<void> {
    searchIndexRebuildPending.value = true;
    searchIndexesErrorMessage.value = null;

    try {
      searchIndex.value = await rebuildSearchIndex(signal);
    } catch (error) {
      searchIndexesErrorMessage.value = describeSearchIndexError(error);
    } finally {
      searchIndexRebuildPending.value = false;
    }
  }

  return {
    loading,
    version,
    lmsConnection,
    errorMessage,
    catalogue,
    catalogueLoading,
    catalogueRebuildPending,
    catalogueErrorMessage,
    searchIndex,
    searchIndexesLoading,
    searchIndexRebuildPending,
    searchIndexesErrorMessage,
    catalogueRebuilding,
    searchIndexesRebuilding,
    load,
    loadCatalogue,
    rebuild,
    loadSearchIndexes,
    rebuildIndex
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

function describeSearchIndexError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The search-index API could not be reached.';
}
