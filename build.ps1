# Robust build script that exits non-zero on failure and validates build/tests automatically.
# Run from repository root: .\src\build.ps1

# Fail fast on unhandled errors
$ErrorActionPreference = 'Stop'

# Determine script directory and solution path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = (Get-Location).ProviderPath }

# Locate a solution file in the repository: prefer a matching Asionyx.sln but fall back to any .sln
$solutionFile = Get-ChildItem -Path $scriptDir -Filter 'Asionyx.sln' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $solutionFile) {
    $solutionFile = Get-ChildItem -Path $scriptDir -Filter '*.sln' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}
if ($solutionFile) {
    $solutionPath = $solutionFile.FullName
} else {
    Write-Host "No .sln found in $scriptDir; falling back to building the AudioRouter project directly."
    $solutionPath = Join-Path $scriptDir 'src\AudioRouter\AudioRouter.csproj'
}

# If the script was invoked from repository root using ./src/build.ps1, ensure we run commands in the src folder
if ((Get-Location).ProviderPath -notlike "*\src") {
    Push-Location $scriptDir
}

# Commands to run (use solutionPath to be explicit)
$commands = @(
    "& dotnet clean `"$solutionPath`"",
    "& dotnet build `"$solutionPath`" --no-restore --verbosity minimal",
    "& dotnet test `"$solutionPath`" --filter 'Category!=Integration' --verbosity normal --no-restore"
)

# Execute commands sequentially and stop on first failure
foreach ($cmd in $commands) {
    Write-Host "Executing: $cmd"
    try {
        Invoke-Expression $cmd
    }
    catch {
        $code = $LASTEXITCODE
        if (-not $code) { $code = 1 }
        Write-Host "Command failed with exit code $code. Stopping execution."
        if ($scriptDir -ne (Get-Location).ProviderPath) { Pop-Location } else { Try { Pop-Location } Catch { } }
        exit $code
    }
}

# Restore location and exit successfully
if ($scriptDir -ne (Get-Location).ProviderPath) { Pop-Location } else { Try { Pop-Location } Catch { } }
Write-Host "Build script finished successfully."

# --- Deployment package & client-driven deploy ---
Write-Host "Creating versioned NuGet package and publish artifacts"
$version = (Get-Date).ToString('yyyy.MM.dd.HHmmss')

# Pack the AudioRouter project as a versioned NuGet
$projectPath = Join-Path $scriptDir 'src\AudioRouter\AudioRouter.csproj'
Write-Host "Packing AudioRouter as version $version"
dotnet pack $projectPath -c Release -p:PackageVersion=$version -o "$scriptDir\artifacts\nupkg"

# Publish the AudioRouter app and create a zip for deployment
$publishDir = Join-Path $scriptDir "publish\audio-router-$version"
Write-Host "Publishing AudioRouter to $publishDir"
dotnet publish $projectPath -c Release -o $publishDir

$zipPath = Join-Path $env:TEMP "audio-router-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Write-Host "Creating zip $zipPath"
Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -Force

# Determine API key and deployment client
$apiKey = $env:DEPLOY_API_KEY; if (-not $apiKey) { $apiKey = 'changeme' }
$deployUrl = 'http://pistomp:5001'
$clientProj = Join-Path $scriptDir 'tools\Asionyx.Tools.Deployment.Client\Asionyx.Tools.Deployment.Client.csproj'

Write-Host "Invoking deployment client to stop remote service"
dotnet run -p $clientProj -- stop --deploy-url $deployUrl --key $apiKey

Write-Host "Invoking deployment client to deploy new version"
dotnet run -p $clientProj -- deploy --zip $zipPath --unit "$scriptDir\deploy\audio-router.service" --deploy-url $deployUrl --key $apiKey

Write-Host "Invoking deployment client to start remote service"
dotnet run -p $clientProj -- start --deploy-url $deployUrl --key $apiKey

Write-Host "Calling remote /info to verify"
dotnet run -p $clientProj -- info --target-url http://pistomp:5000

exit 0
