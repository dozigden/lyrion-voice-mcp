import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const candidateLicenceFiles = [
  'LICENSE',
  'LICENSE.md',
  'LICENSE.txt',
  'LICENCE',
  'LICENCE.md',
  'LICENCE.txt',
  'COPYING'
];

const apiAssetsFile = '../LyrionVoiceMcp.Api/obj/project.assets.json';
const apiTarget = 'net10.0';
const deliverable = 'server-site';
const reviewedNugetLicenceIdentifiers = new Map([
  ['J2N|2.1.0', 'Apache-2.0'],
  ['Lucene.Net|4.8.0-beta00018', 'Apache-2.0 and bundled third-party terms'],
  ['Lucene.Net.Analysis.Common|4.8.0-beta00018', 'Apache-2.0 and bundled third-party terms'],
  ['Lucene.Net.Analysis.Phonetic|4.8.0-beta00018', 'Apache-2.0 and bundled third-party terms']
]);

function sanitisePackageName(packageName) {
  return packageName
    .replace(/^@/, '')
    .replaceAll('/', '-')
    .replace(/[^a-zA-Z0-9.-]+/gu, '-')
    .toLowerCase();
}

function sanitiseVersion(version) {
  return version.replace(/[^a-zA-Z0-9.+-]+/gu, '-').toLowerCase();
}

function npmOutputFileName(packageName) {
  return `npm-${sanitisePackageName(packageName)}.txt`;
}

function nugetOutputFileName(packageName, version) {
  return `nuget-${sanitisePackageName(packageName)}-${sanitiseVersion(version)}.txt`;
}

function normaliseText(sourceText) {
  return sourceText
    .replace(/\r\n?/gu, '\n')
    .split('\n')
    .map(line => line.replace(/[ \t]+$/gu, ''))
    .join('\n')
    .replace(/\n*$/u, '\n');
}

function toPosixPath(filePath) {
  return filePath.split(path.sep).join('/');
}

export function resolveContainedFilePath(rootDirectory, candidatePath) {
  const resolvedRoot = path.resolve(rootDirectory);
  const resolvedCandidate = path.resolve(candidatePath);
  const relativePath = path.relative(resolvedRoot, resolvedCandidate);
  if (!relativePath
      || relativePath === '..'
      || relativePath.startsWith(`..${path.sep}`)
      || path.isAbsolute(relativePath)) {
    throw new Error(`Path escapes its managed directory: ${candidatePath}`);
  }

  return resolvedCandidate;
}

function textSha256(sourceText) {
  return createHash('sha256').update(normaliseText(sourceText)).digest('hex');
}

async function fileExists(filePath) {
  try {
    return (await fs.stat(filePath)).isFile();
  } catch {
    return false;
  }
}

async function directoryExists(directoryPath) {
  try {
    return (await fs.stat(directoryPath)).isDirectory();
  } catch {
    return false;
  }
}

async function writeFileIfChanged(filePath, contents) {
  const desired = Buffer.isBuffer(contents) ? contents : Buffer.from(contents, 'utf8');
  try {
    if ((await fs.readFile(filePath)).equals(desired)) {
      return;
    }
  } catch (error) {
    if (!error || error.code !== 'ENOENT') {
      throw error;
    }
  }

  await fs.writeFile(filePath, desired);
}

async function copyNormalisedText(sourcePath, outputPath) {
  await writeFileIfChanged(outputPath, normaliseText(await fs.readFile(sourcePath, 'utf8')));
}

async function removeFileIfPresent(filePath) {
  try {
    await fs.unlink(filePath);
  } catch (error) {
    if (!error || error.code !== 'ENOENT') {
      throw error;
    }
  }
}

async function findNamedFile(packageRoot, candidateNames) {
  let entries;
  try {
    entries = await fs.readdir(packageRoot);
  } catch {
    return null;
  }

  entries.sort((left, right) => left.localeCompare(right));
  for (const candidateName of candidateNames) {
    const actualName = entries.find(entry => entry === candidateName)
      ?? entries.find(entry => entry.toLowerCase() === candidateName.toLowerCase());
    if (actualName && await fileExists(path.join(packageRoot, actualName))) {
      return path.join(packageRoot, actualName);
    }
  }

  return null;
}

async function findNoticeFiles(packageRoot) {
  const matches = [];

  async function visit(directoryPath, depth) {
    if (depth > 3) {
      return;
    }

    let entries;
    try {
      entries = await fs.readdir(directoryPath, { withFileTypes: true });
    } catch {
      return;
    }

    entries.sort((left, right) => left.name.localeCompare(right.name));
    for (const entry of entries) {
      const entryPath = path.join(directoryPath, entry.name);
      if (entry.isDirectory()) {
        await visit(entryPath, depth + 1);
        continue;
      }

      const normalisedName = path.parse(entry.name).name.toLowerCase().replace(/[^a-z0-9]/gu, '');
      if (entry.isFile() && (
        normalisedName === 'notice'
        || normalisedName.includes('thirdpartynotice')
        || normalisedName.includes('thirdpartylicence')
        || normalisedName.includes('thirdpartylicense')
      )) {
        matches.push(entryPath);
      }
    }
  }

  await visit(packageRoot, 1);
  return matches.sort((left, right) => left.localeCompare(right));
}

function decodeXml(value) {
  return value
    .replaceAll('&amp;', '&')
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'");
}

function readXmlElement(source, elementName) {
  const match = source.match(new RegExp(`<${elementName}(?:\\s[^>]*)?>([\\s\\S]*?)<\\/${elementName}>`, 'iu'));
  return match ? decodeXml(match[1].trim()) : null;
}

function parseNuspecMetadata(nuspecText) {
  const licenceMatch = nuspecText.match(/<license(?:\s+type=["']([^"']+)["'])?>([\s\S]*?)<\/license>/iu);
  return {
    licenceType: licenceMatch?.[1]?.trim().toLowerCase() ?? null,
    licenceValue: licenceMatch ? decodeXml(licenceMatch[2].trim()) : null,
    licenceUrl: readXmlElement(nuspecText, 'licenseUrl'),
    copyright: readXmlElement(nuspecText, 'copyright')
  };
}

function getDeclaredLicence(packageEntry, metadata) {
  if (metadata.licenceType === 'file') {
    return reviewedNugetLicenceIdentifiers.get(`${packageEntry.packageName}|${packageEntry.version}`)
      ?? 'See packaged licence text';
  }

  return metadata.licenceValue ?? metadata.licenceUrl ?? 'unknown';
}

async function findNuspecFile(packageRoot, packageName) {
  const expectedPath = path.join(packageRoot, `${packageName.toLowerCase()}.nuspec`);
  if (await fileExists(expectedPath)) {
    return expectedPath;
  }

  const entries = await fs.readdir(packageRoot);
  const nuspecName = entries.find(entry => entry.toLowerCase().endsWith('.nuspec'));
  return nuspecName ? path.join(packageRoot, nuspecName) : null;
}

function getRuntimeAssets(packageMetadata) {
  const runtimeAssets = [];
  for (const groupName of ['runtime', 'native', 'resources', 'runtimeTargets']) {
    const group = packageMetadata[groupName];
    if (!group || typeof group !== 'object') {
      continue;
    }

    for (const assetPath of Object.keys(group)) {
      if (assetPath !== '_._' && !assetPath.endsWith('/_._') && !assetPath.toLowerCase().endsWith('.pdb')) {
        runtimeAssets.push(assetPath);
      }
    }
  }

  return runtimeAssets.sort((left, right) => left.localeCompare(right));
}

async function loadRuntimeNpmPackages(projectRoot) {
  const inventoryPath = path.join(projectRoot, 'compliance', 'npm-runtime-packages.json');
  const inventory = JSON.parse(await fs.readFile(inventoryPath, 'utf8'));
  if (!Array.isArray(inventory) || inventory.some(packageName => typeof packageName !== 'string')) {
    throw new Error(`Invalid npm runtime inventory at ${inventoryPath}`);
  }

  return [...new Set(inventory)].sort((left, right) => left.localeCompare(right));
}

async function loadRuntimeNugetPackages(projectRoot) {
  const assetsPath = path.resolve(projectRoot, apiAssetsFile);
  const assets = JSON.parse(await fs.readFile(assetsPath, 'utf8'));
  const target = assets.targets?.[apiTarget];
  if (!target) {
    throw new Error(`Runtime asset graph does not contain ${apiTarget}. Run dotnet restore.`);
  }

  const packageFolders = Object.keys(assets.packageFolders ?? {});
  if (packageFolders.length === 0) {
    throw new Error('Runtime asset graph does not identify a NuGet package cache.');
  }

  const packages = [];
  for (const [packageKey, metadata] of Object.entries(target)) {
    if (metadata?.type !== 'package') {
      continue;
    }

    const runtimeAssets = getRuntimeAssets(metadata);
    if (runtimeAssets.length === 0) {
      continue;
    }

    const separatorIndex = packageKey.lastIndexOf('/');
    const packageName = packageKey.slice(0, separatorIndex);
    const version = packageKey.slice(separatorIndex + 1);
    let resolvedPackageRoot = null;
    for (const packageFolder of packageFolders) {
      const candidate = path.join(packageFolder, packageName.toLowerCase(), version.toLowerCase());
      if (await directoryExists(candidate)) {
        resolvedPackageRoot = candidate;
        break;
      }
    }

    packages.push({
      packageName,
      version,
      packageRoot: resolvedPackageRoot,
      runtimeAssets,
      targets: [`LyrionVoiceMcp.Api:${apiTarget}`],
      deliverables: [deliverable]
    });
  }

  return packages.sort((left, right) => {
    const byName = left.packageName.localeCompare(right.packageName);
    return byName || left.version.localeCompare(right.version);
  });
}

async function loadPreviousOutputs(manifestPath) {
  try {
    const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8'));
    return new Set((manifest.copiedLicences ?? []).map(entry => entry.outputFile).filter(Boolean));
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return new Set();
    }

    throw error;
  }
}

function nugetSourcePath(packageName, version, packageRoot, sourcePath) {
  return `NuGet/${packageName}/${version}/${toPosixPath(path.relative(packageRoot, sourcePath))}`;
}

async function loadApacheText(projectRoot) {
  const sourceFile = 'compliance/licence-texts/Apache-2.0.txt';
  const sourcePath = path.join(projectRoot, sourceFile);
  return {
    text: await fs.readFile(sourcePath, 'utf8'),
    sourceFile
  };
}

async function buildExpressionLicence(projectRoot, packageEntry, metadata, apacheText) {
  const header = [
    `NuGet Package: ${packageEntry.packageName}`,
    `Version: ${packageEntry.version}`,
    `Declared Licence Expression: ${metadata.licenceValue}`
  ];
  if (metadata.copyright) {
    header.push(`Copyright: ${metadata.copyright}`);
  }
  header.push('');

  if (metadata.licenceValue === 'MIT' && metadata.copyright) {
    const productLicence = await fs.readFile(path.resolve(projectRoot, '../LICENSE'), 'utf8');
    const permissionIndex = productLicence.indexOf('Permission is hereby granted');
    if (permissionIndex >= 0) {
      return {
        text: [...header, 'MIT License', '', metadata.copyright, '', productLicence.slice(permissionIndex).trim(), ''].join('\n'),
        canonicalSourceFile: '../LICENSE'
      };
    }
  }

  if (metadata.licenceValue === 'Apache-2.0' && apacheText) {
    return {
      text: [...header, normaliseText(apacheText.text).trim(), ''].join('\n'),
      canonicalSourceFile: apacheText.sourceFile
    };
  }

  return null;
}

function sortEntries(entries) {
  entries.sort((left, right) => {
    const byEcosystem = left.ecosystem.localeCompare(right.ecosystem);
    const byName = left.packageName.localeCompare(right.packageName);
    return byEcosystem || byName || left.version.localeCompare(right.version);
  });
}

function getNoticeIdentity(coveredPackages) {
  if (coveredPackages.length === 1) {
    const coveredPackage = coveredPackages[0];
    return {
      packageName: `${coveredPackage.packageName} third-party notices`,
      displayName: `${coveredPackage.packageName} ${coveredPackage.version} — third-party notices`
    };
  }

  const versions = [...new Set(coveredPackages.map(coveredPackage => coveredPackage.version))];
  const versionLabel = versions.length === 1 ? ` ${versions[0]}` : '';
  const packageNameParts = coveredPackages.map(coveredPackage => coveredPackage.packageName.split('.'));
  const commonParts = [...packageNameParts[0]];
  while (commonParts.length > 0 && packageNameParts.some(parts =>
    commonParts.some((part, index) => parts[index] !== part))) {
    commonParts.pop();
  }

  if (commonParts.length >= 2) {
    const familyName = commonParts.join('.');
    return {
      packageName: `${familyName} shared third-party notices`,
      displayName: `${familyName}${versionLabel} — shared third-party notices (${coveredPackages.length} packages)`
    };
  }

  const packageNames = coveredPackages.map(coveredPackage => coveredPackage.packageName).join(', ');
  return {
    packageName: `${packageNames} shared third-party notices`,
    displayName: `${packageNames}${versionLabel} — shared third-party notices`
  };
}

async function main() {
  const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
  const projectRoot = path.resolve(scriptDirectory, '..');
  const outputDirectory = path.join(projectRoot, 'compliance', 'third-party-licenses');
  const publicDirectory = path.join(projectRoot, 'public', 'third-party-licenses');
  const npmManualDirectory = path.join(projectRoot, 'compliance', 'third-party-licenses-manual');
  const nugetManualDirectory = path.join(projectRoot, 'compliance', 'third-party-licenses-manual-nuget');
  const publishPublic = process.argv.includes('--publish-public');
  const strict = process.argv.includes('--strict');

  await Promise.all([
    fs.mkdir(outputDirectory, { recursive: true }),
    fs.mkdir(npmManualDirectory, { recursive: true }),
    fs.mkdir(nugetManualDirectory, { recursive: true }),
    publishPublic ? fs.mkdir(publicDirectory, { recursive: true }) : Promise.resolve()
  ]);

  const [npmPackages, nugetPackages, packageLock] = await Promise.all([
    loadRuntimeNpmPackages(projectRoot),
    loadRuntimeNugetPackages(projectRoot),
    fs.readFile(path.join(projectRoot, 'package-lock.json'), 'utf8').then(JSON.parse)
  ]);
  const apacheText = await loadApacheText(projectRoot);
  const copiedLicences = [];
  const unresolvedPackages = [];
  const managedFiles = new Set();
  const supplementaryNotices = new Map();

  async function registerSupplementaryNotices(packageEntry, primarySourcePath) {
    if (!packageEntry.packageRoot) {
      return;
    }

    for (const noticePath of await findNoticeFiles(packageEntry.packageRoot)) {
      if (primarySourcePath && path.resolve(noticePath) === path.resolve(primarySourcePath)) {
        continue;
      }

      const noticeText = normaliseText(await fs.readFile(noticePath, 'utf8'));
      const digest = textSha256(noticeText);
      const existing = supplementaryNotices.get(digest) ?? {
        text: noticeText,
        sourceFiles: new Set(),
        coveredPackages: new Map(),
        deliverables: new Set()
      };
      existing.sourceFiles.add(nugetSourcePath(
        packageEntry.packageName,
        packageEntry.version,
        packageEntry.packageRoot,
        noticePath
      ));
      existing.coveredPackages.set(`${packageEntry.packageName}|${packageEntry.version}`, {
        packageName: packageEntry.packageName,
        version: packageEntry.version
      });
      existing.deliverables.add(deliverable);
      supplementaryNotices.set(digest, existing);
    }
  }

  const productOutputFile = 'LICENSE-Lyrion-Voice-MCP.txt';
  const productOutputPath = path.join(outputDirectory, productOutputFile);
  const productVersionSource = await fs.readFile(path.resolve(projectRoot, '../Directory.Build.props'), 'utf8');
  const productVersion = productVersionSource.match(/<Version>([^<]+)<\/Version>/u)?.[1]?.trim();
  if (!productVersion) {
    throw new Error('Could not resolve product version from Directory.Build.props.');
  }
  await copyNormalisedText(path.resolve(projectRoot, '../LICENSE'), productOutputPath);
  managedFiles.add(productOutputPath);
  copiedLicences.push({
    ecosystem: 'product',
    entryType: 'licence',
    packageName: 'Lyrion Voice MCP',
    version: productVersion,
    declaredLicence: 'MIT',
    sourceType: 'product',
    sourceFile: '../LICENSE',
    outputFile: toPosixPath(path.relative(projectRoot, productOutputPath)),
    deliverables: [deliverable]
  });

  for (const packageName of npmPackages) {
    const packageRoot = path.join(projectRoot, 'node_modules', ...packageName.split('/'));
    const packageJsonPath = path.join(packageRoot, 'package.json');
    const outputFile = npmOutputFileName(packageName);
    const outputPath = path.join(outputDirectory, outputFile);
    const manualPath = path.join(npmManualDirectory, outputFile);
    managedFiles.add(outputPath);
    await removeFileIfPresent(outputPath);

    try {
      const packageJson = JSON.parse(await fs.readFile(packageJsonPath, 'utf8'));
      const sourcePath = await findNamedFile(packageRoot, candidateLicenceFiles);
      const resolvedSourcePath = await fileExists(manualPath) ? manualPath : sourcePath;
      if (!resolvedSourcePath) {
        unresolvedPackages.push({
          ecosystem: 'npm',
          packageName,
          version: packageJson.version ?? 'unknown',
          declaredLicence: packageJson.license ?? 'unknown',
          reason: 'The installed package did not contain a complete licence text.',
          expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
          resolutionHint: 'Add a reviewed manual licence file at',
          deliverables: [deliverable]
        });
        continue;
      }

      await copyNormalisedText(resolvedSourcePath, outputPath);
      copiedLicences.push({
        ecosystem: 'npm',
        entryType: 'licence',
        packageName,
        version: packageJson.version ?? 'unknown',
        declaredLicence: packageJson.license ?? 'unknown',
        packageIntegrity: packageLock.packages?.[`node_modules/${packageName}`]?.integrity ?? null,
        sourceType: resolvedSourcePath === manualPath ? 'manual' : 'package',
        sourceFile: toPosixPath(path.relative(projectRoot, resolvedSourcePath)),
        outputFile: toPosixPath(path.relative(projectRoot, outputPath)),
        deliverables: [deliverable]
      });
    } catch {
      unresolvedPackages.push({
        ecosystem: 'npm',
        packageName,
        version: 'unknown',
        declaredLicence: 'unknown',
        reason: `Could not read installed package metadata at ${toPosixPath(path.relative(projectRoot, packageJsonPath))}.`,
        deliverables: [deliverable]
      });
    }
  }

  for (const packageEntry of nugetPackages) {
    const outputFile = nugetOutputFileName(packageEntry.packageName, packageEntry.version);
    const outputPath = path.join(outputDirectory, outputFile);
    const manualPath = path.join(nugetManualDirectory, outputFile);
    managedFiles.add(outputPath);
    await removeFileIfPresent(outputPath);

    if (await fileExists(manualPath)) {
      await copyNormalisedText(manualPath, outputPath);
      copiedLicences.push({
        ecosystem: 'nuget',
        entryType: 'licence',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence: 'Manually reviewed licence text',
        sourceType: 'manual',
        sourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
        outputFile: toPosixPath(path.relative(projectRoot, outputPath)),
        runtimeAssets: packageEntry.runtimeAssets,
        targets: packageEntry.targets,
        deliverables: packageEntry.deliverables
      });
      await registerSupplementaryNotices(packageEntry, manualPath);
      continue;
    }

    if (!packageEntry.packageRoot) {
      unresolvedPackages.push({
        ecosystem: 'nuget',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence: 'unknown',
        reason: 'The restored package was not found in a NuGet package cache.',
        expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
        resolutionHint: 'Run dotnet restore or add a reviewed manual licence file at',
        runtimeAssets: packageEntry.runtimeAssets,
        targets: packageEntry.targets,
        deliverables: packageEntry.deliverables
      });
      continue;
    }

    const nuspecPath = await findNuspecFile(packageEntry.packageRoot, packageEntry.packageName);
    if (!nuspecPath) {
      unresolvedPackages.push({
        ecosystem: 'nuget',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence: 'unknown',
        reason: 'The restored package did not contain NuGet metadata.',
        expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
        resolutionHint: 'Add a reviewed manual licence file at',
        deliverables: packageEntry.deliverables
      });
      continue;
    }

    const metadata = parseNuspecMetadata(await fs.readFile(nuspecPath, 'utf8'));
    const declaredLicence = getDeclaredLicence(packageEntry, metadata);
    let packageLicencePath = null;
    let invalidLicencePathReason = null;
    if (metadata.licenceType === 'file' && metadata.licenceValue) {
      try {
        packageLicencePath = resolveContainedFilePath(
          packageEntry.packageRoot,
          path.resolve(packageEntry.packageRoot, ...metadata.licenceValue.replaceAll('\\', '/').split('/'))
        );
      } catch (error) {
        invalidLicencePathReason = error instanceof Error ? error.message : 'The licence path is invalid.';
      }
    } else {
      packageLicencePath = await findNamedFile(packageEntry.packageRoot, candidateLicenceFiles);
    }
    const sourcePath = packageLicencePath && await fileExists(packageLicencePath) ? packageLicencePath : null;

    if (invalidLicencePathReason) {
      unresolvedPackages.push({
        ecosystem: 'nuget',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence,
        reason: `NuGet metadata contains an unsafe licence path. ${invalidLicencePathReason}`,
        expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
        resolutionHint: 'Add a reviewed manual licence file at',
        deliverables: packageEntry.deliverables
      });
      await registerSupplementaryNotices(packageEntry, null);
      continue;
    }

    if (sourcePath) {
      await copyNormalisedText(sourcePath, outputPath);
      copiedLicences.push({
        ecosystem: 'nuget',
        entryType: 'licence',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence,
        declaredLicenceSource: metadata.licenceType === 'file' ? metadata.licenceValue : undefined,
        sourceType: sourcePath === manualPath ? 'manual' : 'nuget-package-file',
        sourceFile: sourcePath === manualPath
          ? toPosixPath(path.relative(projectRoot, sourcePath))
          : nugetSourcePath(packageEntry.packageName, packageEntry.version, packageEntry.packageRoot, sourcePath),
        outputFile: toPosixPath(path.relative(projectRoot, outputPath)),
        runtimeAssets: packageEntry.runtimeAssets,
        targets: packageEntry.targets,
        deliverables: packageEntry.deliverables
      });
    } else if (metadata.licenceType === 'expression' && metadata.licenceValue) {
      const expressionLicence = await buildExpressionLicence(projectRoot, packageEntry, metadata, apacheText);
      if (expressionLicence) {
        await writeFileIfChanged(outputPath, normaliseText(expressionLicence.text));
        copiedLicences.push({
          ecosystem: 'nuget',
          entryType: 'licence',
          packageName: packageEntry.packageName,
          version: packageEntry.version,
          declaredLicence,
          declaredLicenceSource: metadata.licenceValue,
          sourceType: 'nuget-expression-text',
          sourceFile: nugetSourcePath(packageEntry.packageName, packageEntry.version, packageEntry.packageRoot, nuspecPath),
          sourceFiles: [
            nugetSourcePath(packageEntry.packageName, packageEntry.version, packageEntry.packageRoot, nuspecPath),
            expressionLicence.canonicalSourceFile
          ],
          outputFile: toPosixPath(path.relative(projectRoot, outputPath)),
          runtimeAssets: packageEntry.runtimeAssets,
          targets: packageEntry.targets,
          deliverables: packageEntry.deliverables
        });
      } else {
        unresolvedPackages.push({
          ecosystem: 'nuget',
          packageName: packageEntry.packageName,
          version: packageEntry.version,
          declaredLicence,
          reason: `No complete offline text could be resolved for licence expression ${metadata.licenceValue}.`,
          expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
          resolutionHint: 'Add a reviewed manual licence file at',
          deliverables: packageEntry.deliverables
        });
      }
    } else {
      unresolvedPackages.push({
        ecosystem: 'nuget',
        packageName: packageEntry.packageName,
        version: packageEntry.version,
        declaredLicence,
        reason: metadata.licenceUrl
          ? `NuGet metadata provides only an external licence URL: ${metadata.licenceUrl}`
          : 'NuGet metadata did not identify a complete licence text.',
        expectedSourceFile: toPosixPath(path.relative(projectRoot, manualPath)),
        resolutionHint: 'Add a reviewed manual licence file at',
        deliverables: packageEntry.deliverables
      });
    }

    await registerSupplementaryNotices(packageEntry, sourcePath);
  }

  for (const [digest, notice] of [...supplementaryNotices].sort(([left], [right]) => left.localeCompare(right))) {
    const outputFile = `nuget-third-party-notices-${digest.slice(0, 12)}.txt`;
    const outputPath = path.join(outputDirectory, outputFile);
    managedFiles.add(outputPath);
    await writeFileIfChanged(outputPath, notice.text);
    const coveredPackages = [...notice.coveredPackages.values()].sort((left, right) => left.packageName.localeCompare(right.packageName));
    const noticeIdentity = getNoticeIdentity(coveredPackages);
    copiedLicences.push({
      ecosystem: 'nuget',
      entryType: 'notice',
      packageName: noticeIdentity.packageName,
      displayName: noticeIdentity.displayName,
      version: `sha256:${digest.slice(0, 12)}`,
      sourceType: 'nuget-package-notice',
      sourceFile: [...notice.sourceFiles].sort()[0],
      sourceFiles: [...notice.sourceFiles].sort(),
      sourceSha256: digest,
      coveredPackages,
      outputFile: toPosixPath(path.relative(projectRoot, outputPath)),
      deliverables: [...notice.deliverables].sort()
    });
  }

  sortEntries(copiedLicences);
  sortEntries(unresolvedPackages);

  const manifestPath = path.join(outputDirectory, 'MANIFEST.json');
  const previousOutputs = await loadPreviousOutputs(manifestPath);
  for (const previousOutput of previousOutputs) {
    const absolutePath = resolveContainedFilePath(
      outputDirectory,
      path.resolve(projectRoot, previousOutput)
    );
    if (!managedFiles.has(absolutePath)) {
      await removeFileIfPresent(absolutePath);
    }
  }

  const manifest = {
    schemaVersion: 2,
    packageSource: 'Lyrion Voice MCP product licence + production Vite bundle npm modules + runtime assets from the restored API graph',
    copiedLicences,
    unresolvedPackages
  };
  await writeFileIfChanged(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);

  const unresolvedLines = [
    '# Unresolved Third-Party Licence Sources',
    '',
    'This report is generated by `scripts/sync-third-party-licences.mjs`.',
    ''
  ];
  if (unresolvedPackages.length === 0) {
    unresolvedLines.push('All runtime dependencies had a complete offline licence source.');
  } else {
    for (const entry of unresolvedPackages) {
      unresolvedLines.push(`- \`${entry.packageName}\` ${entry.version}: ${entry.reason}`);
      if (entry.expectedSourceFile) {
        unresolvedLines.push(`  ${entry.resolutionHint ?? 'Expected source file'}: \`${entry.expectedSourceFile}\``);
      }
    }
  }
  unresolvedLines.push('');
  await writeFileIfChanged(path.join(outputDirectory, 'UNRESOLVED.md'), unresolvedLines.join('\n'));

  if (publishPublic) {
    const publicManifestPath = path.join(publicDirectory, 'manifest.json');
    const previousPublicOutputs = await loadPreviousOutputs(publicManifestPath);
    const currentPublicFiles = new Set();
    for (const entry of copiedLicences) {
      const fileName = path.basename(entry.outputFile);
      const sourcePath = path.resolve(projectRoot, entry.outputFile);
      const destinationPath = path.join(publicDirectory, fileName);
      await writeFileIfChanged(destinationPath, await fs.readFile(sourcePath));
      currentPublicFiles.add(fileName);
    }

    for (const previousOutput of previousPublicOutputs) {
      const fileName = path.basename(previousOutput);
      if (!currentPublicFiles.has(fileName)) {
        await removeFileIfPresent(path.join(publicDirectory, fileName));
      }
    }

    await writeFileIfChanged(publicManifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  }

  process.stdout.write(`Resolved ${copiedLicences.length} licence file(s).\n`);
  if (unresolvedPackages.length > 0) {
    process.stderr.write(`Could not resolve ${unresolvedPackages.length} licence file(s). See compliance/third-party-licenses/UNRESOLVED.md.\n`);
    if (strict) {
      process.exitCode = 1;
    }
  }
}

const currentScriptPath = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === currentScriptPath) {
  await main();
}
