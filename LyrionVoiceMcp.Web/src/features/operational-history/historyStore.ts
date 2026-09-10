import { defineStore } from 'pinia';
import { remoteResource } from '../../shared/state/remoteResource';
import * as api from './operationalHistoryApi';
export type HistoryKind = 'jobs' | 'errors';
export const useHistoryStore = defineStore('operational-history', () => {
  const list = remoteResource<api.JobPage | api.ErrorLogPage>();
  const detail = remoteResource<api.JobDetails | api.ErrorLog>();
  const mutation = remoteResource<api.Job>();
  async function loadList(kind: HistoryKind, query: string) {
    return list.load(signal => kind === 'jobs' ? api.listJobs(query, signal) : api.listErrors(query, signal));
  }
  async function loadDetail(kind: HistoryKind, id: string) {
    mutation.cancel(); mutation.error.value = null;
    return detail.load(signal => kind === 'jobs' ? api.getJob(id, signal) : api.getError(id, signal));
  }
  async function cancelJob(id: number) {
    if (mutation.loading.value) return;
    const result = await mutation.load(signal => api.cancelJob(id, signal));
    if (result) await loadDetail('jobs', String(id));
  }
  function cancelList() { list.cancel(); }
  function cancelDetail() { detail.cancel(); mutation.cancel(); }
  return { page: list.data, listLoading: list.loading, listError: list.error, record: detail.data, detailLoading: detail.loading,
    detailError: detail.error, mutationError: mutation.error, mutationPending: mutation.loading,
    loadList, loadDetail, cancelJob, cancelList, cancelDetail };
});
