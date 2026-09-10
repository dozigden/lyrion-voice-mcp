import { defineStore } from 'pinia';
import { computed } from 'vue';
import { remoteResource } from '../../shared/state/remoteResource';
import * as api from './operationsApi';
export const useOperationsStore = defineStore('operations', () => {
  const runtime = remoteResource<{ version: api.VersionResponse; lms: api.LmsConnectionResponse }>();
  const catalogue = remoteResource<api.CatalogueStatusResponse>();
  const index = remoteResource<api.SearchIndexStatusResponse>();
  const catalogueMutation = remoteResource<api.CatalogueStatusResponse>();
  const indexMutation = remoteResource<api.SearchIndexStatusResponse>();
  const version = computed(() => runtime.data.value?.version ?? null);
  const lmsConnection = computed(() => runtime.data.value?.lms ?? null);
  const catalogueRebuilding = computed(() => catalogue.data.value?.latestRefresh?.status === 'running');
  const searchIndexesRebuilding = computed(() => ['pending', 'running'].includes(index.data.value?.latestJob?.status ?? ''));
  const catalogueErrorMessage = computed(() => catalogueMutation.error.value ?? catalogue.error.value);
  const searchIndexesErrorMessage = computed(() => indexMutation.error.value ?? index.error.value);
  async function load(signal?: AbortSignal) {
    await runtime.load(async requestSignal => {
      const [version, lms] = await Promise.all([api.getVersion(requestSignal), api.getLmsConnection(requestSignal)]);
      return { version, lms };
    }, false, signal);
  }
  async function loadCatalogue(signal?: AbortSignal) {
    if (catalogueMutation.loading.value) return;
    const result = await catalogue.load(api.getCatalogue, true, signal);
    if (result) catalogueMutation.error.value = null;
  }
  async function loadSearchIndexes(signal?: AbortSignal) {
    if (indexMutation.loading.value) return;
    const result = await index.load(api.getSearchIndex, true, signal);
    if (result) indexMutation.error.value = null;
  }
  async function rebuild(signal?: AbortSignal) {
    if (catalogueMutation.loading.value) return;
    catalogue.cancel();
    const result = await catalogueMutation.load(api.rebuildCatalogue, false, signal);
    if (result) { catalogue.data.value = result; catalogue.error.value = null; }
  }
  async function rebuildIndex(signal?: AbortSignal) {
    if (indexMutation.loading.value) return;
    index.cancel();
    const result = await indexMutation.load(api.rebuildSearchIndex, false, signal);
    if (result) { index.data.value = result; index.error.value = null; }
  }
  return { version, lmsConnection, loading: runtime.loading, errorMessage: runtime.error,
    catalogue: catalogue.data, catalogueLoading: catalogue.loading, catalogueRebuildPending: catalogueMutation.loading, catalogueErrorMessage,
    searchIndex: index.data, searchIndexesLoading: index.loading, searchIndexRebuildPending: indexMutation.loading, searchIndexesErrorMessage,
    catalogueRebuilding, searchIndexesRebuilding, load, loadCatalogue, loadSearchIndexes, rebuild, rebuildIndex };
});
