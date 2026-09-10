import { isRecord, type Decoder } from './decoder';

export async function request<T>(url: string, decode: Decoder<T>, options: RequestInit = {}, acceptedStatuses: number[] = []): Promise<T> {
  const response = await fetch(url, {
    ...options,
    headers: { Accept: 'application/json', ...options.headers }
  });
  let value: unknown;
  try { value = await response.json(); }
  catch { value = undefined; }
  if (!response.ok && !acceptedStatuses.includes(response.status)) {
    throw new Error(problemMessage(value) ?? `${url} returned HTTP ${response.status}.`);
  }
  try { return decode(value); }
  catch { throw new Error(`${url} returned an invalid response.`); }
}
function problemMessage(value: unknown): string | null {
  if (!isRecord(value)) return null;
  if (isRecord(value.errors)) {
    for (const errors of Object.values(value.errors)) {
      if (Array.isArray(errors)) {
        const message = errors.find(item => typeof item === 'string' && item.trim());
        if (typeof message === 'string') return message;
      }
    }
  }
  for (const key of ['message', 'detail', 'title']) {
    if (typeof value[key] === 'string' && value[key].trim()) return value[key];
  }
  return null;
}
