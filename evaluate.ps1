$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $projectRoot
try {
    dotnet run --project LyrionVoiceMcp.Evaluation/LyrionVoiceMcp.Evaluation.csproj -- @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
