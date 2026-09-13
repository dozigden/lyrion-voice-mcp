export function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(date);
}

export function formatAge(value: string, now = Date.now()): string | null {
  const timestamp = new Date(value).getTime();
  if (!Number.isFinite(timestamp)) return null;

  const elapsedMilliseconds = Math.max(0, now - timestamp);
  const minutes = Math.floor(elapsedMilliseconds / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hr ago`;

  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? 'day' : 'days'} ago`;
}

export function duration(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return value < 1000 ? `${value} ms` : `${(value / 1000).toFixed(2)} s`;
}
export function seconds(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return `${Math.floor(value / 60)}:${String(Math.floor(value % 60)).padStart(2, '0')}`;
}
export function label(value: string): string {
  const text = value.replaceAll('_', ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}
export function pretty(value: string | null): string {
  if (value === null) return 'No result recorded.';
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}

export function outcomeSymbol(status: string): string {
  if (status === 'succeeded') return '✓';
  if (['failed', 'tool_error', 'interrupted'].includes(status)) return '!';
  if (status === 'running') return '◷';
  return '–';
}
