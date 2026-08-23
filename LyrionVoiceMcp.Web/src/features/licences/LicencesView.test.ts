import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import LicencesView from './LicencesView.vue';

describe('LicencesView', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads and orders the product and third-party licence texts', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({
        copiedLicences: [
          packageEntry('npm', 'vue', '3.5.41', 'npm-vue.txt'),
          packageEntry('product', 'Lyrion Voice MCP', '0.1.0', 'LICENSE-Lyrion-Voice-MCP.txt'),
          {
            ecosystem: 'nuget',
            entryType: 'notice',
            packageName: 'Lucene.Net shared third-party notices',
            displayName: 'Lucene.Net 4.8.0 — shared third-party notices (2 packages)',
            version: 'sha256:abc',
            outputFile: 'nuget-lucene-notice.txt',
            coveredPackages: [
              { packageName: 'Lucene.Net', version: '4.8.0' },
              { packageName: 'Lucene.Net.Analysis.Common', version: '4.8.0' }
            ]
          }
        ],
        unresolvedPackages: []
      }))
      .mockResolvedValueOnce(textResponse('Vue licence'))
      .mockResolvedValueOnce(textResponse('Product licence'))
      .mockResolvedValueOnce(textResponse('Lucene notice'));
    vi.stubGlobal('fetch', fetchMock);

    const wrapper = mount(LicencesView);
    await flushPromises();

    expect(wrapper.text()).toContain('Lyrion Voice MCP product and third-party licence information');
    expect(wrapper.findAll('summary').map(summary => summary.text())).toEqual([
      'LVMLyrion Voice MCPv0.1.0MIT',
      'NPMvuev3.5.41MIT',
      'NuGetLucene.Net 4.8.0 — shared third-party notices (2 packages)Notice'
    ]);
    expect(wrapper.text()).toContain('Covers: Lucene.Net 4.8.0, Lucene.Net.Analysis.Common 4.8.0');
    expect(fetchMock).toHaveBeenCalledWith('/third-party-licenses/manifest.json', { cache: 'no-store' });
  });

  it('reports a missing manifest', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })));

    const wrapper = mount(LicencesView);
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('HTTP 404');
  });
});

function packageEntry(ecosystem: 'product' | 'npm', packageName: string, version: string, outputFile: string) {
  return { ecosystem, entryType: 'licence', packageName, version, declaredLicence: 'MIT', outputFile };
}

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' }
  });
}

function textResponse(value: string) {
  return new Response(value, { status: 200 });
}
