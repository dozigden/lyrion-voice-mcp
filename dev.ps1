$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project "$projectRoot/LyrionVoiceMcp.Dev/LyrionVoiceMcp.Dev.csproj" -- @args
exit $LASTEXITCODE

