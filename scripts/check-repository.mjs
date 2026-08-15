import { readFileSync, existsSync } from 'node:fs';
import process from 'node:process';

const requiredPaths = [
  'LyrionVoiceMcp.slnx',
  'MCP_CONTRACT.md',
  'LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj',
  'LyrionVoiceMcp.Contracts/LyrionVoiceMcp.Contracts.csproj',
  'LyrionVoiceMcp.Abstractions/LyrionVoiceMcp.Abstractions.csproj',
  'LyrionVoiceMcp.Services/LyrionVoiceMcp.Services.csproj',
  'LyrionVoiceMcp.Lms/LyrionVoiceMcp.Lms.csproj',
  'LyrionVoiceMcp.Lms.Tests/LyrionVoiceMcp.Lms.Tests.csproj',
  'LyrionVoiceMcp.Persistence/LyrionVoiceMcp.Persistence.csproj',
  'LyrionVoiceMcp.Persistence.Tests/LyrionVoiceMcp.Persistence.Tests.csproj',
  'LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj',
  'LyrionVoiceMcp.Evaluation.Tests/LyrionVoiceMcp.Evaluation.Tests.csproj',
  'LyrionVoiceMcp.Web/package.json',
  'LyrionVoiceMcp.Dev/LyrionVoiceMcp.Dev.csproj',
  'scripts/test-fast.sh',
  'scripts/test-full.sh',
  'Dockerfile',
  'THIRD-PARTY-NOTICES.md',
  'compose.yml'
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
  'LyrionVoiceMcp.Persistence',
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
  'LyrionVoiceMcp.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Lms/LyrionVoiceMcp.Lms.csproj', [
  'LyrionVoiceMcp.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Lms.Tests/LyrionVoiceMcp.Lms.Tests.csproj', [
  'LyrionVoiceMcp.Lms'
]);
assertProjectReferences('LyrionVoiceMcp.Persistence/LyrionVoiceMcp.Persistence.csproj', [
  'LyrionVoiceMcp.Abstractions'
]);
assertProjectReferences('LyrionVoiceMcp.Persistence.Tests/LyrionVoiceMcp.Persistence.Tests.csproj', [
  'LyrionVoiceMcp.Persistence'
]);
assertProjectReferences('LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj', [
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Lms',
  'LyrionVoiceMcp.Persistence'
]);
assertProjectReferences('LyrionVoiceMcp.Evaluation.Tests/LyrionVoiceMcp.Evaluation.Tests.csproj', [
  'LyrionVoiceMcp.Evaluation'
]);
assertProjectReferences('LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj', [
  'LyrionVoiceMcp.Abstractions',
  'LyrionVoiceMcp.Contracts',
  'LyrionVoiceMcp.Evaluation',
  'LyrionVoiceMcp.Lms',
  'LyrionVoiceMcp.Persistence',
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
