$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
node "$projectRoot/scripts/run-tests.mjs" @args
exit $LASTEXITCODE

