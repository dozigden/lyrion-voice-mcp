import { defineStore } from 'pinia';
import { ref } from 'vue';
import { remoteResource } from '../../shared/state/remoteResource';
import { getToolCall, listToolCalls, type ToolCall, type ToolCallPage } from '../operational-history/operationalHistoryApi';
export const useToolLogStore = defineStore('tool-log', () => {
  const list = remoteResource<ToolCallPage>();
  const detail = remoteResource<ToolCall>();
  const scrollTop = ref(0);
  const loadList = (query: string) => list.load(signal => listToolCalls(query, signal));
  const loadDetail = (id: string) => detail.load(signal => getToolCall(id, signal));
  function cancel() { list.cancel(); detail.cancel(); }
  return { page: list.data, listLoading: list.loading, listError: list.error,
    call: detail.data, detailLoading: detail.loading, detailError: detail.error,
    scrollTop, loadList, loadDetail, cancel, cancelDetail: detail.cancel };
});
