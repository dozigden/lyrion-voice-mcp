import type { CatalogueStatusResponse, SearchIndexStatusResponse } from './operationsApi';
export function catalogueHeadline(value: CatalogueStatusResponse | null, loading: boolean, error: string | null): string {
  if (error) return 'Unavailable';
  if (!value) return loading ? 'Checking' : 'Unknown';
  if (value.latestRefresh?.status === 'running') return 'Rebuilding';
  if (['failed', 'interrupted', 'cancelled'].includes(value.latestRefresh?.status ?? '')) return value.summary ? 'Ready · attention' : 'Attention';
  return value.summary ? 'Ready' : 'Not built';
}
export function indexHeadline(value: SearchIndexStatusResponse | null, loading: boolean, error: string | null): string {
  if (error) return 'Unavailable';
  if (!value) return loading ? 'Checking' : 'Unknown';
  if (['pending', 'running'].includes(value.latestJob?.status ?? '')) return 'Rebuilding';
  if (['failed', 'cancelled'].includes(value.latestJob?.status ?? '')) return value.artifact ? 'Ready · attention' : 'Attention';
  return value.artifact ? 'Ready' : 'Not built';
}
