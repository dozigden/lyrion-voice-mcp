import vue from '@vitejs/plugin-vue';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [vue(), licenceInventoryPlugin()],
  server: {
    port: 5175,
    proxy: {
      '/api': 'http://127.0.0.1:5600',
      '/mcp': 'http://127.0.0.1:5600'
    }
  },
  test: {
    environment: 'jsdom'
  }
});

function licenceInventoryPlugin(): Plugin {
  const inventoryPath = path.resolve('compliance/npm-runtime-packages.json');

  return {
    name: 'lyrion-voice-mcp-licence-runtime-inventory',
    apply: 'build',
    async generateBundle(_, bundle) {
      const packageNames = new Set<string>();
      for (const output of Object.values(bundle)) {
        if (output.type !== 'chunk') {
          continue;
        }

        for (const moduleId of Object.keys(output.modules)) {
          const packageName = getNodeModulePackageName(moduleId);
          if (packageName) {
            packageNames.add(packageName);
          }
        }
      }

      const actualInventory = [...packageNames].sort((left, right) => left.localeCompare(right));
      if (process.env.LVM_REFRESH_LICENCE_INVENTORY === '1') {
        await writeFile(inventoryPath, `${JSON.stringify(actualInventory, null, 2)}\n`, 'utf8');
      } else {
        const expectedInventory = JSON.parse(await readFile(inventoryPath, 'utf8')) as string[];
        if (JSON.stringify(actualInventory) !== JSON.stringify(expectedInventory)) {
          throw new Error(
            'The production npm licence inventory is stale. '
            + 'Run `npm run refresh:licence-inventory`, then regenerate the licence disclosure.'
          );
        }
      }

      this.emitFile({
        type: 'asset',
        fileName: 'licence-runtime-packages.json',
        source: `${JSON.stringify(actualInventory, null, 2)}\n`
      });
    }
  };
}

function getNodeModulePackageName(moduleId: string): string | null {
  const normalisedId = moduleId.replaceAll('\\', '/');
  const marker = '/node_modules/';
  const packageStart = normalisedId.lastIndexOf(marker);
  if (packageStart < 0) {
    return null;
  }

  const parts = normalisedId.slice(packageStart + marker.length).split('/');
  if (parts[0]?.startsWith('@') && parts[1]) {
    return `${parts[0]}/${parts[1]}`;
  }

  return parts[0] || null;
}
