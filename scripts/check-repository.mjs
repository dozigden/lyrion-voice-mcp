import { readFileSync, existsSync, readdirSync } from 'node:fs';
import process from 'node:process';

const requiredPaths = [
  'LyrionVoiceMcp.slnx',
  '.config/dotnet-tools.json',
  'LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj',
  'LyrionVoiceMcp.Contracts/LyrionVoiceMcp.Contracts.csproj',
  'LyrionVoiceMcp.Abstractions/LyrionVoiceMcp.Abstractions.csproj',
  'LyrionVoiceMcp.Services/LyrionVoiceMcp.Services.csproj',
  'LyrionVoiceMcp.Lms/LyrionVoiceMcp.Lms.csproj',
  'LyrionVoiceMcp.Lms.Tests/LyrionVoiceMcp.Lms.Tests.csproj',
  'LyrionVoiceMcp.Ef/LyrionVoiceMcp.Ef.csproj',
  'LyrionVoiceMcp.Ef.Abstractions/LyrionVoiceMcp.Ef.Abstractions.csproj',
  'LyrionVoiceMcp.Persistence.Tests/LyrionVoiceMcp.Persistence.Tests.csproj',
  'LyrionVoiceMcp.Search/LyrionVoiceMcp.Search.csproj',
  'LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj',
  'LyrionVoiceMcp.Evaluation.Tests/LyrionVoiceMcp.Evaluation.Tests.csproj',
  'LyrionVoiceMcp.Search.Tests/LyrionVoiceMcp.Search.Tests.csproj',
  'LyrionVoiceMcp.Web/package.json',
  'LyrionVoiceMcp.Web/.npmrc',
  'LyrionVoiceMcp.Dev/LyrionVoiceMcp.Dev.csproj',
  'scripts/test-fast.sh',
  'scripts/test-full.sh',
  'scripts/promote-container-digests.sh',
  'Dockerfile',
  'THIRD-PARTY-NOTICES.md',
  'AGENTS/LicenceDisclosure.md',
  'LyrionVoiceMcp.Web/compliance/licence-texts/Apache-2.0.txt',
  'LyrionVoiceMcp.Web/compliance/npm-runtime-packages.json',
  'LyrionVoiceMcp.Web/compliance/third-party-licenses/MANIFEST.json',
  'LyrionVoiceMcp.Web/compliance/third-party-licenses/UNRESOLVED.md',
  'LyrionVoiceMcp.Web/public/third-party-licenses/manifest.json',
  'LyrionVoiceMcp.Web/scripts/sync-third-party-licences.mjs',
  'compose.yml',
  '.github/dependabot.yml'
];

for (const path of requiredPaths) {
  requirePath(path);
}

const agentIndex = readFileSync('AGENTS.md', 'utf8');
const agentLinks = [...agentIndex.matchAll(/\((AGENTS\/[^)]+\.md)\)/gu)].map(match => match[1]);
if (agentLinks.length === 0) {
  fail('AGENTS.md does not link to any topic guidance.');
}

for (const link of agentLinks) {
  requirePath(link);
}

const apiProject = readFileSync('LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj', 'utf8');
if (!apiProject.includes('ModelContextProtocol.AspNetCore') || !apiProject.includes('Version="2.1.0"')) {
  fail('The API project must pin ModelContextProtocol.AspNetCore 2.1.0.');
}

const npmConfiguration = readFileSync('LyrionVoiceMcp.Web/.npmrc', 'utf8');
for (const setting of ['ignore-scripts=true', 'min-release-age=7']) {
  if (!npmConfiguration.split(/\r?\n/u).includes(setting)) {
    fail(`LyrionVoiceMcp.Web/.npmrc must set ${setting}.`);
  }
}

const dependabot = readFileSync('.github/dependabot.yml', 'utf8');
for (const ecosystem of ['npm', 'nuget', 'docker', 'github-actions']) {
  const entry = dependabot.match(new RegExp(
    `  - package-ecosystem: "${ecosystem}"(?<body>[\\s\\S]*?)(?=\\n  - package-ecosystem:|$)`,
    'u'
  ));
  if (!entry?.groups.body.match(/\n    cooldown:\n      default-days: 7(?:\n|$)/u)) {
    fail(`Dependabot must apply a seven-day cooldown to ${ecosystem} updates.`);
  }
}

const buildProperties = readFileSync('Directory.Build.props', 'utf8');
for (const setting of [
  '<NuGetAudit>true</NuGetAudit>',
  '<NuGetAuditLevel>high</NuGetAuditLevel>',
  '<NuGetAuditMode>all</NuGetAuditMode>'
]) {
  if (!buildProperties.includes(setting)) {
    fail(`Directory.Build.props must contain ${setting}.`);
  }
}

const dockerfile = readFileSync('Dockerfile', 'utf8');
for (const match of dockerfile.matchAll(/^FROM\s+(?<image>\S+)/gmu)) {
  if (!/@sha256:[0-9a-f]{64}$/u.test(match.groups.image)) {
    fail(`Docker base image is not pinned by digest: ${match.groups.image}.`);
  }
}
if (!dockerfile.includes('LyrionVoiceMcp.Web/.npmrc ./') || !dockerfile.includes('npm ci --ignore-scripts')) {
  fail('The container frontend install must copy the npm policy and explicitly disable install scripts.');
}

const workflowDirectory = '.github/workflows';
const workflowFiles = readdirSync(workflowDirectory)
  .filter(path => path.endsWith('.yml') || path.endsWith('.yaml'))
  .map(path => `${workflowDirectory}/${path}`);

for (const workflowPath of workflowFiles) {
  const workflow = readFileSync(workflowPath, 'utf8');
  for (const match of workflow.matchAll(/^\s*uses:\s*(?<reference>\S+)(?<suffix>.*)$/gmu)) {
    const reference = match.groups.reference;
    if (!reference.startsWith('./') && !/@[0-9a-f]{40}$/u.test(reference)) {
      fail(`${workflowPath} uses a mutable external action reference: ${reference}.`);
    }
    if (!reference.startsWith('./') && !/^\s+# v\d+(?:\.\d+){0,2}\s*$/u.test(match.groups.suffix)) {
      fail(`${workflowPath} must retain a human-readable version comment for ${reference}.`);
    }
  }
}

const ciWorkflow = readFileSync('.github/workflows/ci.yml', 'utf8');
if (!ciWorkflow.includes('npm audit --audit-level=high --package-lock-only')) {
  fail('CI must block high and critical npm vulnerabilities.');
}
if (/secrets\./u.test(ciWorkflow)) {
  fail('Credential-free CI must not reference repository secrets.');
}

const promotionScript = readFileSync('scripts/promote-container-digests.sh', 'utf8');
const orderedPromotionPhases = [
  'write_staged_platforms "$ghcr_image"',
  'write_staged_platforms "$dockerhub_image"',
  'create_candidate "$ghcr_image"',
  'create_candidate "$dockerhub_image"',
  'publish_verified_candidate "$ghcr_image"',
  'publish_verified_candidate "$dockerhub_image"'
];
let previousPromotionPhase = -1;
for (const marker of orderedPromotionPhases) {
  const promotionPhase = promotionScript.indexOf(marker);
  if (promotionPhase <= previousPromotionPhase) {
    fail('Container publication must validate both registries and their candidates before moving public tags.');
  }
  previousPromotionPhase = promotionPhase;
}

for (const workflowPath of [
  '.github/workflows/publish-image-tag.yml',
  '.github/workflows/publish-image-nightly.yml'
]) {
  const workflow = readFileSync(workflowPath, 'utf8');
  const publicationBuilds = [...workflow.matchAll(/uses: docker\/build-push-action@/gu)];
  if (publicationBuilds.length !== 1) {
    fail(`${workflowPath} must build each platform only once for publication.`);
  }
  if (!workflow.includes('uses: ./.github/workflows/ci.yml')
    || !workflow.includes('Smoke-test exact staged digests')
    || !workflow.includes('scripts/promote-container-digests.sh')
    || !workflow.includes('group: container-publication')
    || !workflow.includes('provenance: mode=max')
    || !workflow.includes('sbom: true')) {
    fail(`${workflowPath} must validate, smoke-test staged digests, and promote through the shared script.`);
  }
}

const program = readFileSync('LyrionVoiceMcp.Api/Program.cs', 'utf8');
for (const route of ['/api/health', '/api/version', '/mcp']) {
  if (!program.includes(route) && !readFileSync('LyrionVoiceMcp.Api/Endpoints/OperationalEndpoints.cs', 'utf8').includes(route)) {
    fail(`The documented route ${route} is not mapped.`);
  }
}

const architecture = readFileSync('AGENTS/Architecture.md', 'utf8');
for (const project of [
  'LyrionVoiceMcp.Api',
  'LyrionVoiceMcp.Contracts',
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Services',
  'LyrionVoiceMcp.Lms',
  'LyrionVoiceMcp.Ef',
  'LyrionVoiceMcp.Ef.Abstractions',
  'LyrionVoiceMcp.Search',
  'LyrionVoiceMcp.Evaluation',
  'LyrionVoiceMcp.Web',
  'LyrionVoiceMcp.Dev'
]) {
  if (!architecture.includes(`\`${project}\``)) {
    fail(`AGENTS/Architecture.md does not describe ${project}.`);
  }
}

assertProjectReferences('LyrionVoiceMcp.Contracts/LyrionVoiceMcp.Contracts.csproj', []);
assertProjectReferences('LyrionVoiceMcp.Abstractions/LyrionVoiceMcp.Abstractions.csproj', []);
assertProjectReferences('LyrionVoiceMcp.Services/LyrionVoiceMcp.Services.csproj', [
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Ef.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Lms/LyrionVoiceMcp.Lms.csproj', [
  'LyrionVoiceMcp.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Lms.Tests/LyrionVoiceMcp.Lms.Tests.csproj', [
  'LyrionVoiceMcp.Lms'
]);
assertProjectReferences('LyrionVoiceMcp.Ef.Abstractions/LyrionVoiceMcp.Ef.Abstractions.csproj', []);
assertProjectReferences('LyrionVoiceMcp.Ef/LyrionVoiceMcp.Ef.csproj', [
  'LyrionVoiceMcp.Ef.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Persistence.Tests/LyrionVoiceMcp.Persistence.Tests.csproj', [
  'LyrionVoiceMcp.Ef',
  'LyrionVoiceMcp.Ef.Abstractions',
  'LyrionVoiceMcp.Services'
]);
assertProjectReferences('LyrionVoiceMcp.Search/LyrionVoiceMcp.Search.csproj', [
  'LyrionVoiceMcp.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Search.Tests/LyrionVoiceMcp.Search.Tests.csproj', [
  'LyrionVoiceMcp.Search'
]);
assertProjectReferences('LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj', [
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Lms',
  'LyrionVoiceMcp.Search'
]);
assertProjectReferences('LyrionVoiceMcp.Evaluation.Tests/LyrionVoiceMcp.Evaluation.Tests.csproj', [
  'LyrionVoiceMcp.Evaluation',
  'LyrionVoiceMcp.Search'
]);
assertProjectReferences('LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj', [
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Contracts',
  'LyrionVoiceMcp.Ef',
  'LyrionVoiceMcp.Lms',
  'LyrionVoiceMcp.Search',
  'LyrionVoiceMcp.Services'
]);

function requirePath(path) {
  if (!existsSync(path)) {
    fail(`Required repository path is missing: ${path}`);
  }
}

function assertProjectReferences(projectPath, expectedProjects) {
  const project = readFileSync(projectPath, 'utf8');
  const actualProjects = [...project.matchAll(/ProjectReference Include="[^"\\/]+[\\/](?<project>[^"\\/]+)[\\/][^"\\/]+\.csproj"/gu)]
    .map(match => match.groups.project)
    .sort();
  const expected = [...expectedProjects].sort();
  if (JSON.stringify(actualProjects) !== JSON.stringify(expected)) {
    fail(`${projectPath} has unexpected project dependencies: ${actualProjects.join(', ') || '(none)'}.`);
  }
}

function fail(message) {
  process.stderr.write(`${message}\n`);
  process.exit(1);
}
