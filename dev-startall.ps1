$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $projectRoot 'LyrionVoiceMcp.Api/LyrionVoiceMcp.Api.csproj'
$webDirectory = Join-Path $projectRoot 'LyrionVoiceMcp.Web'

if (-not (Test-Path (Join-Path $webDirectory 'node_modules'))) {
    throw 'Run npm ci in LyrionVoiceMcp.Web first.'
}

dotnet build $apiProject -maxcpucount:1 -nodeReuse:false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousUrls = $env:ASPNETCORE_URLS
$previousLoadLocalSettings = $env:LyrionVoiceMcpDevelopment__LoadLocalSettings
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5600'
$env:LyrionVoiceMcpDevelopment__LoadLocalSettings = 'true'
$api = Start-Process dotnet -ArgumentList @('run', '--no-launch-profile', '--no-build', '--project', $apiProject) -PassThru -NoNewWindow
$env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
$env:ASPNETCORE_URLS = $previousUrls
$env:LyrionVoiceMcpDevelopment__LoadLocalSettings = $previousLoadLocalSettings
$web = Start-Process npm.cmd -ArgumentList @('run', 'dev') -WorkingDirectory $webDirectory -PassThru -NoNewWindow

try {
    while (-not $api.HasExited -and -not $web.HasExited) {
        Start-Sleep -Milliseconds 250
    }
}
finally {
    foreach ($process in @($api, $web)) {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
    }
}
