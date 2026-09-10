import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';
const eslint = new ESLint();
describe('template conventions', () => {
  it.each([
    ['<template><p>{{ a ? b ? c : d : e }}</p></template>', 'nested template ternaries'],
    ['<template><button @click="handle($event)">Run</button></template>', 'inline $event']
  ])('rejects a prohibited template expression', async (code, message) => {
    const [result] = await eslint.lintText(code, { filePath: 'src/ConventionFixture.vue' });
    expect(result!.messages.some(item => item.ruleId === 'vue/no-restricted-syntax' && item.message.includes(message))).toBe(true);
  });
  it('accepts named handlers and separate conditional branches', async () => {
    const [result] = await eslint.lintText('<template><button @click="handle">Run</button><p v-if="ready">Ready</p><p v-else>Waiting</p></template>', { filePath: 'src/ConventionFixture.vue' });
    expect(result!.errorCount).toBe(0);
  });
});
