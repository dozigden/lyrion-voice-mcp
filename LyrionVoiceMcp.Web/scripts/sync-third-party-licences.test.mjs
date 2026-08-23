import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { resolveContainedFilePath } from './sync-third-party-licences.mjs';

describe('licence disclosure path safety', () => {
  const managedDirectory = path.resolve('/tmp/licence-disclosure/managed');

  it('accepts files within the managed directory', () => {
    const candidate = path.join(managedDirectory, 'package.txt');

    expect(resolveContainedFilePath(managedDirectory, candidate)).toBe(candidate);
  });

  it('rejects parent traversal and absolute paths outside the managed directory', () => {
    expect(() => resolveContainedFilePath(
      managedDirectory,
      path.resolve(managedDirectory, '../LICENSE')
    )).toThrow('Path escapes its managed directory');
    expect(() => resolveContainedFilePath(managedDirectory, '/tmp/unrelated.txt'))
      .toThrow('Path escapes its managed directory');
  });

  it('rejects the managed directory itself as a file target', () => {
    expect(() => resolveContainedFilePath(managedDirectory, managedDirectory))
      .toThrow('Path escapes its managed directory');
  });
});
