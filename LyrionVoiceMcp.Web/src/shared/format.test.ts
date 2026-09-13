import { describe, expect, it } from 'vitest';
import { formatAge } from './format';

describe('shared date formatting', () => {
  it('formats recent, singular, older and future timestamps as ages', () => {
    const now = new Date('2026-01-03T13:00:00Z').getTime();

    expect(formatAge('2026-01-03T12:59:31Z', now)).toBe('just now');
    expect(formatAge('2026-01-03T12:59:00Z', now)).toBe('1 min ago');
    expect(formatAge('2026-01-03T12:00:00Z', now)).toBe('1 hr ago');
    expect(formatAge('2026-01-02T13:00:00Z', now)).toBe('1 day ago');
    expect(formatAge('2026-01-04T13:00:00Z', now)).toBe('just now');
    expect(formatAge('not-a-date', now)).toBeNull();
  });
});
