import { defineStore } from 'pinia';
import { ref } from 'vue';
import {
  browseSearchObservations,
  getSearchObservation,
  saveSearchReview,
  type SaveSearchReviewRequest,
  type SearchObservationDetail,
  type SearchObservationFilters,
  type SearchObservationPage
} from './searchObservationsApi';

export const useSearchObservationsStore = defineStore('search-observations', () => {
  const page = ref<SearchObservationPage | null>(null);
  const selected = ref<SearchObservationDetail | null>(null);
  const loading = ref(false);
  const saving = ref(false);
  const errorMessage = ref<string | null>(null);

  async function browse(filters: SearchObservationFilters, signal?: AbortSignal): Promise<void> {
    loading.value = true;
    errorMessage.value = null;
    try {
      page.value = await browseSearchObservations(filters, signal);
    } catch (error) {
      errorMessage.value = describe(error);
    } finally {
      loading.value = false;
    }
  }

  async function load(id: string, signal?: AbortSignal): Promise<void> {
    loading.value = true;
    errorMessage.value = null;
    selected.value = null;
    try {
      selected.value = await getSearchObservation(id, signal);
    } catch (error) {
      errorMessage.value = describe(error);
    } finally {
      loading.value = false;
    }
  }

  async function save(id: string, review: SaveSearchReviewRequest): Promise<boolean> {
    saving.value = true;
    errorMessage.value = null;
    try {
      selected.value = await saveSearchReview(id, review);
      return true;
    } catch (error) {
      errorMessage.value = describe(error);
      return false;
    } finally {
      saving.value = false;
    }
  }

  return { page, selected, loading, saving, errorMessage, browse, load, save };
});

function describe(error: unknown): string {
  return error instanceof Error && error.message.trim() ? error.message : 'The search observation API could not be reached.';
}
