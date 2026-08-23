<template>
  <main class="licences-page">
    <header class="licences-header">
      <p class="eyebrow">Open-source software</p>
      <h1>Licences</h1>
      <p>Lyrion Voice MCP product and third-party licence information for this server and site.</p>
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
import { computed, onMounted, ref } from 'vue';

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

onMounted(loadLicences);

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
    const manifestResponse = await fetch('/third-party-licenses/manifest.json', { cache: 'no-store' });
    if (!manifestResponse.ok) {
      throw new Error(`Failed to load the licence manifest (HTTP ${manifestResponse.status}).`);
    }

    const parsedManifest = await manifestResponse.json() as LicenceManifest;
    if (!Array.isArray(parsedManifest.copiedLicences) || !Array.isArray(parsedManifest.unresolvedPackages)) {
      throw new Error('The licence manifest is invalid.');
    }
    manifest.value = parsedManifest;

    const entries = await Promise.all(parsedManifest.copiedLicences.map(async entry => {
      const fileName = entry.outputFile.split('/').pop();
      if (!fileName) {
        return { ...entry, text: '', errorMessage: 'The manifest entry has no output file.' };
      }

      try {
        const response = await fetch(`/third-party-licenses/${encodeURIComponent(fileName)}`, { cache: 'no-store' });
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
    licenceEntries.value = entries;
  } catch (error) {
    manifestError.value = error instanceof Error ? error.message : 'Failed to load licence information.';
  } finally {
    loading.value = false;
  }
}
</script>

<style scoped>
.licences-page {
  width: min(960px, calc(100% - 40px));
  margin: 0 auto;
  padding: 48px 0 64px;
}

.licences-header h1,
.licences-header p,
.unresolved h2 {
  margin: 0;
}

.licences-header h1 {
  margin-top: 5px;
  font: 600 clamp(2rem, 6vw, 3.4rem)/1 var(--font-display);
}

.licences-header > p:last-child,
.licences-state {
  margin-top: 12px;
  color: var(--text-muted);
}

.eyebrow {
  color: var(--accent);
  font-size: .72rem;
  font-weight: 700;
  letter-spacing: .14em;
  text-transform: uppercase;
}

.licences-list {
  display: grid;
  gap: 10px;
  margin-top: 28px;
}

.licence-entry,
.unresolved {
  border: 1px solid var(--border);
  border-radius: 12px;
  background: var(--surface);
}

.licence-entry summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 15px 17px;
  cursor: pointer;
}

.licence-entry summary:hover {
  color: var(--accent-soft);
}

.badge {
  padding: 3px 7px;
  border: 1px solid var(--border-strong);
  border-radius: 999px;
  color: var(--text-muted);
  font-size: .72rem;
}

.badge--ecosystem {
  color: var(--accent);
  letter-spacing: .04em;
}

.licence-entry pre {
  max-height: 28rem;
  margin: 0 17px 17px;
  padding: 15px;
  overflow: auto;
  border: 1px solid var(--border);
  border-radius: 9px;
  background: rgba(15, 14, 11, .7);
  color: var(--text);
  font: .79rem/1.45 ui-monospace, "Cascadia Mono", Consolas, monospace;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}

.licence-coverage {
  margin: 0 17px 10px;
  color: var(--text-muted);
  font-size: .82rem;
}

.licences-error {
  color: var(--danger-text);
}

.licence-entry .licences-error {
  margin: 0 17px 17px;
}

.unresolved {
  margin-top: 24px;
  padding: 17px;
}

.unresolved h2 {
  font: 600 1.2rem var(--font-display);
}

.unresolved ul {
  display: grid;
  gap: 10px;
  margin: 14px 0 0;
  padding-left: 20px;
}

.unresolved li span {
  display: block;
  margin-top: 3px;
  color: var(--text-muted);
}
</style>
