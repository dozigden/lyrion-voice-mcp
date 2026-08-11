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
