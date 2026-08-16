export interface HealthResponse {
  status: string;
}

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

export async function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const result = await getJson('/api/health', signal);
  if (!isRecord(result) || typeof result.status !== 'string') {
    throw new Error('/api/health returned an invalid response.');
  }

  return { status: result.status };
}

export async function getVersion(signal?: AbortSignal): Promise<VersionResponse> {
  const result = await getJson('/api/version', signal);
  if (!isRecord(result)
    || typeof result.version !== 'string'
    || typeof result.channel !== 'string'
    || typeof result.build !== 'string'
    || typeof result.commit !== 'string') {
    throw new Error('/api/version returned an invalid response.');
  }

  return {
    version: result.version,
    channel: result.channel,
    build: result.build,
    commit: result.commit
  };
}

export async function getLmsConnection(signal?: AbortSignal): Promise<LmsConnectionResponse> {
  const result = await getJson('/api/lms', signal);
  if (!isRecord(result)
    || !isLmsStatus(result.status)
    || !isNullableString(result.serverId)
    || !isNullableString(result.baseUrl)
    || !isNullableString(result.serverVersion)
    || typeof result.message !== 'string') {
    throw new Error('/api/lms returned an invalid response.');
  }

  return {
    status: result.status,
    serverId: result.serverId,
    baseUrl: result.baseUrl,
    serverVersion: result.serverVersion,
    message: result.message
  };
}

export async function getCatalogue(signal?: AbortSignal): Promise<CatalogueStatusResponse> {
  return parseCatalogueStatus(await getJson('/api/catalogue', signal));
}

export async function rebuildCatalogue(signal?: AbortSignal): Promise<CatalogueStatusResponse> {
  const response = await fetch('/api/catalogue/refresh', {
    method: 'POST',
    headers: {
      Accept: 'application/json'
    },
    signal
  });

  if (response.status !== 202 && response.status !== 409) {
    throw new Error(`/api/catalogue/refresh returned HTTP ${response.status}.`);
  }

  return parseCatalogueStatus(await response.json() as unknown);
}

export async function getSearchIndexes(signal?: AbortSignal): Promise<SearchIndexStatusResponse[]> {
  const value = await getJson('/api/evaluation/indexes', signal);
  if (!Array.isArray(value) || !value.every(isSearchIndexStatus)) {
    throw new Error('/api/evaluation/indexes returned an invalid response.');
  }

  return value;
}

export async function rebuildSearchIndex(
  resolver: string,
  signal?: AbortSignal
): Promise<SearchIndexStatusResponse> {
  const url = `/api/evaluation/indexes/${encodeURIComponent(resolver)}/rebuild`;
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      Accept: 'application/json'
    },
    signal
  });

  const value = await response.json() as unknown;
  if (response.status === 409) {
    const message = isRecord(value) && typeof value.message === 'string'
      ? value.message
      : 'The search-index rebuild was rejected.';
    throw new Error(message);
  }

  if (response.status !== 202 || !isSearchIndexStatus(value)) {
    throw new Error(`${url} returned HTTP ${response.status}.`);
  }

  return value;
}

async function getJson(url: string, signal?: AbortSignal): Promise<unknown> {
  const response = await fetch(url, {
    headers: {
      Accept: 'application/json'
    },
    signal
  });

  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}.`);
  }

  return await response.json() as unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isLmsStatus(value: unknown): value is LmsConnectionResponse['status'] {
  return value === 'not_configured' || value === 'online' || value === 'unavailable';
}

function parseCatalogueStatus(value: unknown): CatalogueStatusResponse {
  if (!isRecord(value)
    || !isNullableCatalogueSummary(value.summary)
    || !isNullableCatalogueRefresh(value.latestRefresh)) {
    throw new Error('/api/catalogue returned an invalid response.');
  }

  return {
    summary: value.summary,
    latestRefresh: value.latestRefresh
  };
}

function isNullableCatalogueSummary(value: unknown): value is CatalogueSummaryResponse | null {
  if (value === null) {
    return true;
  }

  return isRecord(value)
    && typeof value.sourceId === 'string'
    && typeof value.provider === 'string'
    && isNullableString(value.sourceRevision)
    && isNullableString(value.sourceVersion)
    && typeof value.capturedAt === 'string'
    && isNullableString(value.sourceLastScanAt)
    && typeof value.refreshedAt === 'string'
    && isNumber(value.artistCount)
    && isNumber(value.albumCount)
    && isNumber(value.genreCount)
    && isNumber(value.trackCount)
    && isNumber(value.virtualLibraryCount)
    && isNumber(value.warningCount);
}

function isNullableCatalogueRefresh(value: unknown): value is CatalogueRefreshRunResponse | null {
  if (value === null) {
    return true;
  }

  return isRecord(value)
    && typeof value.id === 'string'
    && isCatalogueRefreshStatus(value.status)
    && typeof value.startedAt === 'string'
    && isNullableString(value.completedAt)
    && isNullableNumber(value.durationMilliseconds)
    && isNullableString(value.failureMessage)
    && Array.isArray(value.logs)
    && value.logs.every(isCatalogueRefreshLog);
}

function isCatalogueRefreshLog(value: unknown): value is CatalogueRefreshLogResponse {
  return isRecord(value)
    && isNumber(value.id)
    && typeof value.occurredAt === 'string'
    && isCatalogueLogLevel(value.level)
    && typeof value.message === 'string'
    && isNullableNumber(value.processedCount)
    && isNullableNumber(value.totalCount);
}

function isCatalogueRefreshStatus(value: unknown): value is CatalogueRefreshRunResponse['status'] {
  return value === 'running'
    || value === 'succeeded'
    || value === 'failed'
    || value === 'cancelled'
    || value === 'interrupted';
}

function isCatalogueLogLevel(value: unknown): value is CatalogueRefreshLogResponse['level'] {
  return value === 'information' || value === 'warning' || value === 'error';
}

function isSearchIndexStatus(value: unknown): value is SearchIndexStatusResponse {
  return isRecord(value)
    && typeof value.resolver === 'string'
    && isNullableSearchIndexArtifact(value.artifact)
    && isNullableSearchIndexJob(value.latestJob);
}

function isNullableSearchIndexArtifact(
  value: unknown
): value is SearchIndexArtifactResponse | null {
  if (value === null) {
    return true;
  }

  return isRecord(value)
    && typeof value.resolverVersion === 'string'
    && typeof value.catalogueRefreshId === 'string'
    && typeof value.builtAt === 'string'
    && isNumber(value.candidateCount)
    && isNumber(value.preparationDurationMilliseconds)
    && isNumber(value.indexSizeBytes);
}

function isNullableSearchIndexJob(value: unknown): value is SearchIndexJobResponse | null {
  if (value === null) {
    return true;
  }

  return isRecord(value)
    && isNumber(value.id)
    && isSearchIndexJobStatus(value.status)
    && isNullableString(value.startedAt)
    && isNullableString(value.completedAt)
    && isNullableString(value.errorMessage);
}

function isSearchIndexJobStatus(value: unknown): value is SearchIndexJobResponse['status'] {
  return value === 'pending'
    || value === 'running'
    || value === 'succeeded'
    || value === 'failed'
    || value === 'cancelled';
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || isNumber(value);
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}
