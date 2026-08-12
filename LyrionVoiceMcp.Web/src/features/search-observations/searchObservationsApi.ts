export type SearchClassification =
  | 'good'
  | 'wrong_order'
  | 'no_match'
  | 'ambiguous'
  | 'transcription_error'
  | 'other';

export interface SearchObservationSummary {
  id: string;
  createdAt: string;
  originalQuery: string;
  resolver: string;
  resolverVersion: string;
  status: 'completed' | 'failed';
  resultCount: number;
  selectedPosition: number | null;
  totalDurationMilliseconds: number;
  classification: SearchClassification | null;
  includeInEvaluation: boolean;
}

export interface SearchRequestObservation {
  source: string;
  command: string;
  status: 'completed' | 'failed';
  failureMessage: string | null;
  durationMilliseconds: number;
  resultCount: number;
}

export interface SearchCandidateObservation {
  position: number;
  correlationId: string;
  kind: string;
  title: string;
  artist: string | null;
  album: string | null;
  selectedAt: string | null;
}

export interface SearchReview {
  classification: SearchClassification;
  expectedCorrelationId: string | null;
  expectedKind: string | null;
  expectedTitle: string | null;
  expectedArtist: string | null;
  expectedAlbum: string | null;
  notes: string | null;
  includeInEvaluation: boolean;
  reviewedAt: string;
}

export interface SearchObservationDetail {
  id: string;
  createdAt: string;
  originalQuery: string;
  normalisedQuery: string;
  requestedKind: string | null;
  provider: string;
  collection: string;
  resolver: string;
  resolverVersion: string;
  status: 'completed' | 'failed';
  failureMessage: string | null;
  totalDurationMilliseconds: number;
  retrievalDurationMilliseconds: number;
  processingDurationMilliseconds: number;
  requests: SearchRequestObservation[];
  candidates: SearchCandidateObservation[];
  review: SearchReview | null;
  retentionDays: number;
}

export interface SearchObservationPage {
  items: SearchObservationSummary[];
  total: number;
  offset: number;
  limit: number;
  retentionDays: number;
}

export interface SearchObservationFilters {
  query: string;
  review: 'all' | 'unreviewed' | 'reviewed';
  result: 'all' | 'no-results' | 'selected' | 'failed';
  offset?: number;
  limit?: number;
}

export interface SaveSearchReviewRequest {
  classification: SearchClassification;
  expectedCorrelationId: string | null;
  expectedKind: string | null;
  expectedTitle: string | null;
  expectedArtist: string | null;
  expectedAlbum: string | null;
  notes: string | null;
  includeInEvaluation: boolean;
}

export async function browseSearchObservations(
  filters: SearchObservationFilters,
  signal?: AbortSignal
): Promise<SearchObservationPage> {
  const parameters = new URLSearchParams({
    review: filters.review,
    result: filters.result,
    offset: String(filters.offset ?? 0),
    limit: String(filters.limit ?? 50)
  });
  if (filters.query.trim()) parameters.set('query', filters.query.trim());
  const value = await requestJson(`/api/search-observations?${parameters}`, { signal });
  if (!isRecord(value) || !Array.isArray(value.items)
    || !value.items.every(isSummary) || !isNumber(value.total)
    || !isNumber(value.offset) || !isNumber(value.limit) || !isNumber(value.retentionDays)) {
    throw new Error('Search observation list returned an invalid response.');
  }
  return value as unknown as SearchObservationPage;
}

export async function getSearchObservation(id: string, signal?: AbortSignal): Promise<SearchObservationDetail> {
  const value = await requestJson(`/api/search-observations/${encodeURIComponent(id)}`, { signal });
  if (!isDetail(value)) throw new Error('Search observation detail returned an invalid response.');
  return value;
}

export async function saveSearchReview(
  id: string,
  review: SaveSearchReviewRequest,
  signal?: AbortSignal
): Promise<SearchObservationDetail> {
  const value = await requestJson(`/api/search-observations/${encodeURIComponent(id)}/review`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(review),
    signal
  });
  if (!isDetail(value)) throw new Error('Saved search review returned an invalid response.');
  return value;
}

async function requestJson(url: string, init: RequestInit): Promise<unknown> {
  const response = await fetch(url, { ...init, headers: { Accept: 'application/json', ...init.headers } });
  if (!response.ok) throw new Error(`${url} returned HTTP ${response.status}.`);
  return await response.json() as unknown;
}

function isSummary(value: unknown): boolean {
  return isRecord(value) && typeof value.id === 'string' && typeof value.createdAt === 'string'
    && typeof value.originalQuery === 'string' && typeof value.resolver === 'string'
    && typeof value.resolverVersion === 'string' && (value.status === 'completed' || value.status === 'failed')
    && isNumber(value.resultCount) && (value.selectedPosition === null || isNumber(value.selectedPosition))
    && isNumber(value.totalDurationMilliseconds)
    && (value.classification === null || isClassification(value.classification))
    && typeof value.includeInEvaluation === 'boolean';
}

function isDetail(value: unknown): value is SearchObservationDetail {
  return isRecord(value) && typeof value.id === 'string' && typeof value.createdAt === 'string'
    && typeof value.originalQuery === 'string' && typeof value.normalisedQuery === 'string'
    && typeof value.provider === 'string' && typeof value.collection === 'string'
    && typeof value.resolver === 'string' && typeof value.resolverVersion === 'string'
    && (value.status === 'completed' || value.status === 'failed')
    && isNumber(value.totalDurationMilliseconds) && isNumber(value.retrievalDurationMilliseconds)
    && isNumber(value.processingDurationMilliseconds) && Array.isArray(value.requests)
    && Array.isArray(value.candidates) && isNumber(value.retentionDays)
    && (value.review === null || isRecord(value.review));
}

function isClassification(value: unknown): value is SearchClassification {
  return ['good', 'wrong_order', 'no_match', 'ambiguous', 'transcription_error', 'other'].includes(String(value));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}
