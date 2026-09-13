import type { CatalogueStatusResponse, SearchIndexStatusResponse } from './operationsApi';
import { formatAge } from '../../shared/format';

export interface HeaderMaintenanceStatus {
  label: string;
  timestamp: string | null;
  ready: boolean;
}

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

export function catalogueHeaderStatus(
  value: CatalogueStatusResponse | null,
  loading: boolean,
  error: string | null,
  now = Date.now()
): HeaderMaintenanceStatus {
  return headerStatus(catalogueHeadline(value, loading, error), value?.summary?.refreshedAt ?? null, now);
}

export function indexHeaderStatus(
  value: SearchIndexStatusResponse | null,
  loading: boolean,
  error: string | null,
  now = Date.now()
): HeaderMaintenanceStatus {
  return headerStatus(indexHeadline(value, loading, error), value?.artifact?.builtAt ?? null, now);
}

function headerStatus(headline: string, timestamp: string | null, now: number): HeaderMaintenanceStatus {
  const ready = headline === 'Ready';
  const attention = headline === 'Ready · attention';
  const age = timestamp && (ready || attention) ? formatAge(timestamp, now) : null;
  if (!age) return { label: headline, timestamp: null, ready };
  return { label: attention ? `${age} · attention` : age, timestamp, ready };
}
