export function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(date);
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

export function callTime(value: string): string {
  return new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(new Date(value));
}
export function callDay(value: string): string {
  const date = new Date(value);
  if (date.toDateString() === new Date().toDateString()) return '';
  return new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' }).format(date);
}
export function outcomeSymbol(status: string): string {
  if (status === 'succeeded') return '✓';
  if (['failed', 'tool_error', 'interrupted'].includes(status)) return '!';
  if (status === 'running') return '◷';
  return '–';
}
