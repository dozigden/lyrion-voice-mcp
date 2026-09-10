<template>
  <main class="licences-page">
    <header class="licences-header">
      <h1>Licences</h1>
    </header>

    <p v-if="loading" class="licences-state" role="status">Loading licences…</p>
    <p v-else-if="manifestError" class="licences-error" role="alert">{{ manifestError }}</p>

    <template v-else>
      <section v-if="licenceEntries.length > 0" class="licences-list" aria-label="Licence texts">
        <details
          v-for="entry in licenceEntries"
          :key="`${entry.ecosystem}:${entry.packageName}:${entry.version}`"
          class="licence-entry"
        >
          <summary>
            <span class="badge badge--ecosystem">{{ ecosystemLabel(entry.ecosystem) }}</span>
            <strong>{{ entry.displayName ?? entry.packageName }}</strong>
            <span v-if="!entry.displayName" class="badge">v{{ entry.version }}</span>
            <span class="badge">{{ entryBadge(entry) }}</span>
          </summary>
          <p v-if="entry.errorMessage" class="licences-error" role="alert">{{ entry.errorMessage }}</p>
          <template v-else>
            <p v-if="entry.coveredPackages?.length" class="licence-coverage">
              Covers: {{ coveredPackageLabels(entry.coveredPackages).join(', ') }}
            </p>
            <pre>{{ entry.text }}</pre>
          </template>
        </details>
      </section>
      <p v-else class="licences-state">No licence entries were found.</p>

      <section v-if="unresolvedPackages.length > 0" class="unresolved" aria-labelledby="unresolved-title">
        <h2 id="unresolved-title">Some licence information is unavailable</h2>
        <ul>
          <li v-for="entry in unresolvedPackages" :key="`${entry.ecosystem}:${entry.packageName}:${entry.version}`">
            <strong>{{ entry.packageName }} {{ entry.version }}</strong>
            <span>{{ entry.reason }}</span>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue';
import { array, object, oneOf, optional, string } from '../../shared/api/decoder';
import { request } from '../../shared/api/http';

type LicenceEcosystem = 'product' | 'npm' | 'nuget';

type CopiedLicence = {
  ecosystem: LicenceEcosystem;
  entryType?: 'licence' | 'notice';
  packageName: string;
  displayName?: string;
  version: string;
  declaredLicence?: string;
  outputFile: string;
  coveredPackages?: CoveredPackage[];
};

type CoveredPackage = {
  packageName: string;
  version: string;
};

type UnresolvedPackage = {
  ecosystem: LicenceEcosystem;
  packageName: string;
  version: string;
  reason: string;
};

type LicenceManifest = {
  copiedLicences: CopiedLicence[];
  unresolvedPackages: UnresolvedPackage[];
};

type LicenceEntry = CopiedLicence & {
  text: string;
  errorMessage: string | null;
};

const loading = ref(true);
const manifestError = ref<string | null>(null);
const manifest = ref<LicenceManifest | null>(null);
const licenceEntries = ref<LicenceEntry[]>([]);
const unresolvedPackages = computed(() => manifest.value?.unresolvedPackages ?? []);

const controller = new AbortController();
onMounted(loadLicences);
onBeforeUnmount(() => controller.abort());
const ecosystem = oneOf('product', 'npm', 'nuget');
const manifestDecoder = object({
  copiedLicences: array(object({ ecosystem, entryType: optional(oneOf('licence', 'notice')), packageName: string,
    displayName: optional(string), version: string, declaredLicence: optional(string), outputFile: string,
    coveredPackages: optional(array(object({ packageName: string, version: string }))) })),
  unresolvedPackages: array(object({ ecosystem, packageName: string, version: string, reason: string }))
});

function ecosystemLabel(ecosystem: LicenceEcosystem) {
  if (ecosystem === 'product') {
    return 'LVM';
  }

  return ecosystem === 'nuget' ? 'NuGet' : 'NPM';
}

function entryBadge(entry: CopiedLicence) {
  return entry.entryType === 'notice' ? 'Notice' : entry.declaredLicence ?? 'Licence';
}

function coveredPackageLabels(coveredPackages: CoveredPackage[]) {
  return coveredPackages.map(coveredPackage =>
    `${coveredPackage.packageName} ${coveredPackage.version}`
  );
}

async function loadLicences() {
  try {
    const parsedManifest = await request('/third-party-licenses/manifest.json', manifestDecoder, { cache: 'no-store', signal: controller.signal });
    if (controller.signal.aborted) return;
    manifest.value = parsedManifest;

    const entries = await Promise.all(parsedManifest.copiedLicences.map(async entry => {
      const fileName = entry.outputFile.split('/').pop();
      if (!fileName) {
        return { ...entry, text: '', errorMessage: 'The manifest entry has no output file.' };
      }

      try {
        const response = await fetch(`/third-party-licenses/${encodeURIComponent(fileName)}`, { cache: 'no-store', signal: controller.signal });
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        return { ...entry, text: await response.text(), errorMessage: null };
      } catch (error) {
        const detail = error instanceof Error ? ` ${error.message}` : '';
        return { ...entry, text: '', errorMessage: `Could not load this licence text.${detail}` };
      }
    }));

    entries.sort((left, right) => {
      if (left.ecosystem === 'product' && right.ecosystem !== 'product') {
        return -1;
      }
      if (right.ecosystem === 'product' && left.ecosystem !== 'product') {
        return 1;
      }

      return left.ecosystem.localeCompare(right.ecosystem)
        || left.packageName.localeCompare(right.packageName)
        || left.version.localeCompare(right.version);
    });
    if (!controller.signal.aborted) licenceEntries.value = entries;
  } catch (error) {
    if (controller.signal.aborted) return;
    manifestError.value = error instanceof Error ? error.message : 'Failed to load licence information.';
  } finally {
    loading.value = false;
  }
}
</script>

<style scoped>
.licences-page { width:min(1200px,100%); margin:0 auto; padding:28px 34px 40px; }.licences-header h1 { font-size:22px; margin:0; }.licences-state { color:var(--text-muted); }.licences-list { margin-top:24px; }.licence-entry { border-bottom:1px solid var(--border); }.licence-entry summary { display:flex; flex-wrap:wrap; align-items:center; gap:12px; padding:14px 18px; cursor:pointer; }.licence-entry[open] summary { background:var(--heading-band); color:var(--selection); }.licence-entry summary:hover { background:var(--selection-hover); }.badge { font-size:14px; color:var(--text-muted); }.badge--ecosystem { color:var(--selection); min-width:48px; }.licence-entry pre { max-height:32rem; margin:16px 18px; }.licence-coverage { margin:14px 18px; font-size:14px; color:var(--text-muted); }.licences-error { color:var(--danger-text); }.licence-entry .licences-error { margin:14px 18px; }.unresolved { margin-top:24px; padding:18px; background:var(--heading-band); }.unresolved h2 { font-size:18px; }.unresolved li span { display:block; color:var(--text-muted); }
@media(max-width:720px) { .licences-page { padding:24px 18px; } }
</style>
