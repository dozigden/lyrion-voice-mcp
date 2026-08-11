import { spawnSync } from 'node:child_process';
import process from 'node:process';

const args = new Set(process.argv.slice(2));
const mode = args.has('--full') ? 'full' : 'fast';
const explicitLane = [...args].find(argument => argument.endsWith('-only'));
const startedAt = Date.now();

const lanes = resolveLanes(mode, explicitLane);
const steps = [];

run('Repository guidance', 'node', ['scripts/check-repository.mjs']);

if (lanes.backend) {
  run('Restore', 'dotnet', [
    'restore',
    'LyrionVoiceMcp.slnx',
    '--locked-mode',
    '-maxcpucount:1',
    '-nodeReuse:false'
  ]);
  run('Build', 'dotnet', [
    'build',
    'LyrionVoiceMcp.slnx',
    '--configuration',
    'Release',
    '--no-restore',
    '-maxcpucount:1',
    '-nodeReuse:false'
  ]);
}

if (lanes.api) {
  runTestAssembly('API tests', 'LyrionVoiceMcp.Api.Tests');
}

if (lanes.services) {
  runTestAssembly('Services tests', 'LyrionVoiceMcp.Services.Tests');
}

if (lanes.lms) {
  runTestAssembly('LMS tests', 'LyrionVoiceMcp.Lms.Tests');
}

if (lanes.dev) {
  runTestAssembly('Dev tests', 'LyrionVoiceMcp.Dev.Tests');
}

if (lanes.web) {
  run('Web checks', npmCommand(), ['run', 'check'], 'LyrionVoiceMcp.Web');
}

const elapsedSeconds = ((Date.now() - startedAt) / 1000).toFixed(1);
const detail = steps.map(step => `${step.name} ${step.seconds.toFixed(1)}s`).join('; ');
process.stdout.write(`[test-${mode}] PASS (${steps.length} steps, ${elapsedSeconds}s total; ${detail})\n`);

function resolveLanes(selectedMode, lane) {
  if (lane) {
    return {
      backend: lane !== '--web-only',
      api: lane === '--api-only' || lane === '--backend-only',
      services: lane === '--services-only' || lane === '--backend-only',
      lms: lane === '--lms-only' || lane === '--backend-only',
      dev: lane === '--dev-only' || lane === '--backend-only',
      web: lane === '--web-only'
    };
  }

  if (selectedMode === 'full') {
    return { backend: true, api: true, services: true, lms: true, dev: true, web: true };
  }

  const changed = changedFiles();
  if (changed.length === 0) {
    return { backend: true, api: true, services: true, lms: true, dev: true, web: true };
  }

  const web = changed.some(path => path.startsWith('LyrionVoiceMcp.Web/'));
  const api = changed.some(path => path.startsWith('LyrionVoiceMcp.Api') || path.startsWith('LyrionVoiceMcp.Contracts'));
  const services = changed.some(path => path.startsWith('LyrionVoiceMcp.Services') || path.startsWith('LyrionVoiceMcp.Abstractions'));
  const lms = changed.some(path => path.startsWith('LyrionVoiceMcp.Lms') || path.startsWith('LyrionVoiceMcp.Abstractions'));
  const dev = changed.some(path => path.startsWith('LyrionVoiceMcp.Dev'));
  const global = changed.some(path => !path.includes('/') || path.startsWith('scripts/') || path.startsWith('.github/'));

  return {
    backend: api || services || lms || dev || global,
    api: api || global,
    services: services || global,
    lms: lms || global,
    dev: dev || global,
    web: web || global
  };
}

function changedFiles() {
  const result = spawnSync('git', ['diff', '--name-only', 'HEAD'], {
    cwd: process.cwd(),
    encoding: 'utf8'
  });
  if (result.status !== 0) {
    return [];
  }

  return result.stdout.split(/\r?\n/u).filter(Boolean);
}

function runTestAssembly(name, projectName) {
  run(name, 'dotnet', [
    `${projectName}/bin/Release/net10.0/${projectName}.dll`,
    '--no-progress',
    '--no-ansi'
  ]);
}

function run(name, command, commandArgs, cwd = process.cwd()) {
  const stepStartedAt = Date.now();
  const result = spawnSync(command, commandArgs, {
    cwd,
    encoding: 'utf8',
    env: {
      ...process.env,
      CI: process.env.CI ?? '1',
      NUGET_HTTP_CACHE_PATH: process.env.NUGET_HTTP_CACHE_PATH ?? '/tmp/lyrion-voice-mcp-nuget-http-cache'
    }
  });
  const seconds = (Date.now() - stepStartedAt) / 1000;
  steps.push({ name, seconds });

  if (result.status === 0) {
    return;
  }

  process.stderr.write(`[test-${mode}] FAIL: ${name} (${seconds.toFixed(1)}s)\n`);
  process.stderr.write(result.stdout ?? '');
  process.stderr.write(result.stderr ?? '');
  process.exit(result.status ?? 1);
}

function npmCommand() {
  return process.platform === 'win32' ? 'npm.cmd' : 'npm';
}
