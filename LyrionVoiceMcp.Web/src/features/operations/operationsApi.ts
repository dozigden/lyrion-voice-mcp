import { array, date, nullable, number, object, oneOf, string } from '../../shared/api/decoder';
import { request } from '../../shared/api/http';

export interface VersionResponse {
  version: string;
  channel: string;
  build: string;
  commit: string;
}

export interface LmsConnectionResponse {
  status: 'not_configured' | 'online' | 'unavailable';
  serverId: string | null;
  baseUrl: string | null;
  serverVersion: string | null;
  message: string;
}

export interface CatalogueSummaryResponse {
  sourceId: string;
  provider: string;
  sourceRevision: string | null;
  sourceVersion: string | null;
  capturedAt: string;
  sourceLastScanAt: string | null;
  refreshedAt: string;
  artistCount: number;
  albumCount: number;
  genreCount: number;
  trackCount: number;
  virtualLibraryCount: number;
  warningCount: number;
}

export interface CatalogueRefreshLogResponse {
  id: number;
  occurredAt: string;
  level: 'information' | 'warning' | 'error';
  message: string;
  processedCount: number | null;
  totalCount: number | null;
}

export interface CatalogueRefreshRunResponse {
  id: string;
  status: 'running' | 'succeeded' | 'failed' | 'cancelled' | 'interrupted';
  startedAt: string;
  completedAt: string | null;
  durationMilliseconds: number | null;
  failureMessage: string | null;
  logs: CatalogueRefreshLogResponse[];
}

export interface CatalogueStatusResponse {
  summary: CatalogueSummaryResponse | null;
  latestRefresh: CatalogueRefreshRunResponse | null;
}

export interface SearchIndexArtifactResponse {
  resolverVersion: string;
  catalogueRefreshId: string;
  builtAt: string;
  candidateCount: number;
  preparationDurationMilliseconds: number;
  indexSizeBytes: number;
}

export interface SearchIndexJobResponse {
  id: number;
  status: 'pending' | 'running' | 'succeeded' | 'failed' | 'cancelled';
  startedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

export interface SearchIndexStatusResponse {
  resolver: string;
  artifact: SearchIndexArtifactResponse | null;
  latestJob: SearchIndexJobResponse | null;
}

const ns = nullable(string), nd = nullable(date), nn = nullable(number);
const version = object({ version: string, channel: string, build: string, commit: string });
const lms = object({ status: oneOf('not_configured', 'online', 'unavailable'), serverId: ns, baseUrl: ns, serverVersion: ns, message: string });
const catalogue = object({
  summary: nullable(object({ sourceId: string, provider: string, sourceRevision: ns, sourceVersion: ns,
    capturedAt: date, sourceLastScanAt: nd, refreshedAt: date, artistCount: number, albumCount: number,
    genreCount: number, trackCount: number, virtualLibraryCount: number, warningCount: number })),
  latestRefresh: nullable(object({ id: string, status: oneOf('running', 'succeeded', 'failed', 'cancelled', 'interrupted'),
    startedAt: date, completedAt: nd, durationMilliseconds: nn, failureMessage: ns,
    logs: array(object({ id: number, occurredAt: date, level: oneOf('information', 'warning', 'error'), message: string,
      processedCount: nn, totalCount: nn })) }))
});
const index = object({ resolver: string,
  artifact: nullable(object({ resolverVersion: string, catalogueRefreshId: string, builtAt: date, candidateCount: number,
    preparationDurationMilliseconds: number, indexSizeBytes: number })),
  latestJob: nullable(object({ id: number, status: oneOf('pending', 'running', 'succeeded', 'failed', 'cancelled'),
    startedAt: nd, completedAt: nd, errorMessage: ns }))
});
export const getVersion = (signal?: AbortSignal): Promise<VersionResponse> => request('/api/version', version, { signal });
export const getLmsConnection = (signal?: AbortSignal): Promise<LmsConnectionResponse> => request('/api/lms', lms, { signal });
export const getCatalogue = (signal?: AbortSignal): Promise<CatalogueStatusResponse> => request('/api/catalogue', catalogue, { signal });
export const rebuildCatalogue = (signal?: AbortSignal): Promise<CatalogueStatusResponse> => request('/api/catalogue/refresh', catalogue, { method: 'POST', signal }, [409]);
export const getSearchIndex = (signal?: AbortSignal): Promise<SearchIndexStatusResponse> => request('/api/search/index', index, { signal });
export const rebuildSearchIndex = (signal?: AbortSignal): Promise<SearchIndexStatusResponse> => request('/api/search/index/rebuild', index, { method: 'POST', signal });
