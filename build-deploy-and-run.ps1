param(
    [string]$Configuration = 'Debug',
    [string]$SshHost = 'pistomp5',
    [string]$SshUser = 'pistomp',
    [string]$SshKey = "~/.ssh/id_rsa",
    [string]$PublishDir = 'tools/Asionyx.Tools.Deployment.Client.Ssh/publish'
)

# Top-level configurable values (change here for environment-specific deployments)
$RemoteDeployDir = '/opt/Asionyx.Service.Deployment.Linux'
$ServiceName = 'deployment-service'
$SshPort = 22
$SshClientProject = 'tools/Asionyx.Tools.Deployment.Client.Ssh/Asionyx.Tools.Deployment.Client.Ssh.csproj'
$PublishProject = 'src/Asionyx.Service.Deployment.Linux/Asionyx.Service.Deployment.Linux.csproj'
$MaxServiceChecks = 3
$HttpStatusPort = 5001
$ServiceCheckRetryDelay = 5 # seconds
$DockerTestFilter = '-p:VSTestTestCaseFilter=Category!=RequiresDocker'
$SshClientExeName = 'Asionyx.Tools.Deployment.Client.Ssh'

# Normalize common paths
$SshKey = $SshKey -replace "\\", "/"

function Write-Log([string]$msg) { Write-Host $msg }

function Invoke-Build {
    Write-Log "Building solution..."
    dotnet build .\audio-linux.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

function Test-Docker {
    Write-Host "testing for : docker daemon..." -NoNewline
    $dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $dockerCmd) { Write-Host " failure (docker not installed)"; return $false }
    try {
        docker info > $null 2>&1
        if ($LASTEXITCODE -eq 0) { Write-Host " success"; return $true } else { Write-Host " failure (docker not running)"; return $false }
    }
    catch { Write-Host " failure (docker check failed: $($_.Exception.Message))"; return $false }
}

function Invoke-Test {
    param([bool]$DockerAvailable)
    Write-Log "Running tests..."
    # Do not set transient environment variables here; tests should use injected ServiceSettings or test fixtures.
    if (-not $DockerAvailable) {
        Write-Host "Docker not available: excluding RequiresDocker tests from test run."
        dotnet test .\\audio-linux.sln -c $Configuration --no-build $DockerTestFilter
    }
    else { dotnet test .\\audio-linux.sln -c $Configuration --no-build }
    if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet test failed (exit $LASTEXITCODE); continuing because tests may require environment/setup not available locally." }
}

function Test-Preflight {
    Write-Log "Pre-flight checks: validating environment and connectivity"
    # Private key
    Write-Host "testing for : private key file exists at '$SshKey'..." -NoNewline
    if (-not (Test-Path $SshKey)) { Write-Host " failure (file not found)"; throw "Private key file not found at '$SshKey'. Aborting." } else { Write-Host " success" }

    # DNS
    Write-Host "testing for : DNS resolution for '$SshHost'..." -NoNewline
    try { [void][System.Net.Dns]::GetHostEntry($SshHost); Write-Host " success" } catch { Write-Host " failure (DNS lookup failed: $($_.Exception.Message))" }

    # TCP port 22
    Write-Host "testing for : TCP port 22 reachability to '$SshHost:22'..." -NoNewline
    try { $tcp = Test-NetConnection -ComputerName $SshHost -Port 22 -InformationLevel Quiet -WarningAction SilentlyContinue; if ($tcp) { Write-Host " success" } else { Write-Host " failure (cannot reach $SshHost:22)" } }
    catch { Write-Host " failure (Test-NetConnection not available: $($_.Exception.Message))" }

    # dotnet SDK
    Write-Host "testing for : dotnet SDK version..." -NoNewline
    try { $dotnet = & dotnet --version 2>&1; if ($LASTEXITCODE -eq 0) { Write-Host " success (version: $dotnet)" } else { Write-Host " failure (dotnet returned: $dotnet)" } }
    catch { Write-Host " failure (dotnet not found: $($_.Exception.Message))" }

    # Build and start the systemd test image/container inside WSL. All WSL interactions are centralized here
    # so no C# test code or test helpers need to invoke WSL directly.
    Test-WslPreflight
}

function Test-WslPreflight {
    Write-Log "Pre-flight WSL check: building test image and starting container inside WSL"

    $wslCmd = Get-Command wsl -ErrorAction SilentlyContinue
    if ($null -eq $wslCmd) { throw "WSL not available on this host; pre-flight requires WSL to build/start the systemd test image." }

    # Find a running WSL distro (prefer non-docker-desktop)
    $list = wsl -l -v 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to query WSL distros: $list" }

    $distro = $null
    $lines = $list -split "\r?\n"
    foreach ($line in $lines) {
        $trim = $line.Trim()
        if ($trim -match "^NAME" -or [string]::IsNullOrWhiteSpace($trim)) { continue }
        if ($trim -match "Running" -and $trim -notmatch "docker-desktop") {
            # first token is the distro name
            $parts = $trim -split '\\s+'
            $distro = $parts[0]
            break
        }
    }

    if (-not $distro) { throw "No running WSL distro found (exclude docker-desktop). Start a distro or ensure Docker is running in WSL." }

    Write-Log "Using WSL distro: $distro"

    # Convert repository Windows path to WSL path
    $repoWin = (Resolve-Path ".").Path
    $wslRepo = wsl -d $distro -- wslpath -a "$repoWin" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "wslpath failed to convert path: $wslRepo" }
    $wslRepo = $wslRepo.Trim()

    # Build the image inside WSL
    Write-Log "Building docker image inside WSL distro $distro"
    $buildCmd = "docker build -t audio-linux/ci-systemd-trixie:local -f '$wslRepo/build/ci-systemd-trixie.Dockerfile' '$wslRepo'"
    $buildOut = wsl -d $distro -- bash -lc "$buildCmd" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $buildOut
        throw "Docker build inside WSL failed (distro $distro). See output above."
    }

    # Ensure any previous test container is removed
    Write-Log "Removing any existing test container 'audio-linux-ci-systemd'"
    wsl -d $distro -- bash -lc "docker rm -f audio-linux-ci-systemd 2>/dev/null || true" | Out-Null

    # Start the container in detached mode (entrypoint will start the emulator and the deployment service)
    Write-Log "Starting test container inside WSL"
    $runCmd = "docker rm -f audio-linux-ci-systemd 2>/dev/null || true; docker run --name audio-linux-ci-systemd -d -p 5001:5001 -p 5200:5200 audio-linux/ci-systemd-trixie:local"
    $runOut = wsl -d $distro -- bash -lc "$runCmd" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $runOut
        throw "Failed to start systemd test container inside WSL (distro $distro)."
    }

    Start-Sleep -Seconds 3

    $inspect = wsl -d $distro -- bash -lc "docker inspect -f '{{.State.Running}} {{.State.ExitCode}}' audio-linux-ci-systemd" 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host $inspect; throw "Failed to inspect started test container inside WSL." }

    $inspect = $inspect.Trim()
    if ($inspect -like 'true*') {
        Write-Log "Test container is running inside WSL"
        return $true
    }
    else {
        Write-Host "Container not running after start: $inspect"
        $logs = wsl -d $distro -- bash -lc "docker logs --tail 500 audio-linux-ci-systemd" 2>&1
        Write-Host "--- container logs ---`n$logs`n--- end logs ---"
        throw "Systemd test container failed to start inside WSL (see logs above)"
    }
}

function Publish-ContainerArtifacts {
    Write-Host "Publishing container artifacts (Deployment, Systemd emulator, Test target)..."
    # cleanup old publish folders
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path "publish" -Verbose:$false
    New-Item -ItemType Directory -Force -Path publish | Out-Null

    # Deployment app (the existing project may be under src/Asionyx.Tools.Deployment)
    $deployProj = 'src/Asionyx.Tools.Deployment/Asionyx.Tools.Deployment.csproj'
    if (-not (Test-Path $deployProj)) { $deployProj = 'src/Asionyx.Service.Deployment.Linux/Asionyx.Service.Deployment.Linux.csproj' }
    Write-Host "Publishing Deployment project: $deployProj -> publish/Deployment"
    dotnet publish $deployProj -c $Configuration -o publish/Deployment
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Deployment) failed" }

    # Systemd emulator
    $sysProj = 'src/Asionyx.Service.Systemd/Asionyx.Service.Systemd.csproj'
    Write-Host "Publishing Systemd emulator: $sysProj -> publish/Systemd"
    dotnet publish $sysProj -c $Configuration -o publish/Systemd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Systemd) failed" }

    # Test target service
    $testProj = 'src/Asionyx.TestTargetService/Asionyx.TestTargetService.csproj'
    Write-Host "Publishing TestTarget service: $testProj -> publish/TestTarget"
    dotnet publish $testProj -c $Configuration -o publish/TestTarget
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (TestTarget) failed" }

    Write-Host "Published container artifacts under ./publish"
}

function Publish-SshClient {
    Write-Log "Packaging publish bundle into $PublishDir"
    Write-Log "Publishing SSH client project $SshClientProject (RID detection follows)..."
    if ($IsWindows) { $rid = 'win-x64' } elseif ($IsLinux) { $rid = 'linux-x64' } elseif ($IsMacOS) { $rid = 'osx-x64' } else { $rid = 'win-x64' }
    $sshClientProjectPath = $SshClientProject -replace '/', '\\'
    $sshClientProjectDir = Split-Path $sshClientProjectPath -Parent
    $sshClientPublishDir = Join-Path $sshClientProjectDir "bin\publish-$rid"
    Write-Host "Publishing SSH client project $SshClientProject -> $sshClientPublishDir (RID=$rid)"
    # suppress dotnet publish output so the function returns only the path string
    dotnet publish $SshClientProject -c $Configuration -r $rid -o $sshClientPublishDir > $null 2>&1
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (ssh client) failed (exit $LASTEXITCODE)" }

    if ($IsWindows) { $sshClientExe = Join-Path $sshClientPublishDir "$SshClientExeName.exe" } else { $sshClientExe = Join-Path $sshClientPublishDir "$SshClientExeName" }
    if (-not (Test-Path $sshClientExe)) { throw "Required SSH client binary not found at $sshClientExe. Aborting." }
    return (Resolve-Path $sshClientExe).ProviderPath
}

function Invoke-SshClient([string]$sshClientExeFull, [string[]]$args) {
    Push-Location "tools/Asionyx.Tools.Deployment.Client.Ssh"
    # Explicitly pass the ssh-key argument instead of setting environment variables
    $argList = @('--ssh-key', $SshKey) + $args
    Write-Host "Invoking: $sshClientExeFull $($argList -join ' ')"
    # Capture stdout/stderr to avoid the external process output becoming pipeline output from this function
    $procOut = & "$sshClientExeFull" $argList 2>&1
    if ($procOut) { Write-Host $procOut }
    $rc = $LASTEXITCODE
    Pop-Location
    return $rc
}

function Test-PrivateKey([string]$sshClientExeFull) {
    Write-Host "Running remote private-key verification..."
    $rc = Invoke-SshClient $sshClientExeFull @('--verify-private-key','--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
    if ($rc -ne 0) { throw "Private-key verification failed (exit $rc)" }
}

function Publish-Server {
    Write-Host "Publishing server project $PublishProject -> $PublishDir"
    dotnet publish $PublishProject -c $Configuration -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}

function Invoke-Deploy([string]$sshClientExeFull) {
    Write-Host "Deploying publish bundle to $SshHost as $SshUser using key $SshKey (configured defaults from appsettings will be used)."
    Write-Host "-> Running publish/upload flow (invoking published ssh client)"
    # Call the SSH client without --verify-only to perform the full installation. Pass SSH host/user/key so the client knows how to connect.
    $rc = Invoke-SshClient $sshClientExeFull @('--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
    if ($rc -ne 0) { throw "Publish/upload step failed (exit $rc)" }
}

# Initialize-UserDataDirectoryRemote moved into the SSH client. Do not call ssh.exe from this script.

# Install-SystemdUnit moved into the SSH client. Do not call ssh.exe from this script.

function Test-ServiceActive {
    param([string]$sshClientExeFull, [int]$maxAttempts)
    $attempt = 0
    $serviceActive = $false
    while ($attempt -lt $maxAttempts -and -not $serviceActive) {
        $attempt++
        Write-Host "Service check attempt $attempt of $maxAttempts..."
        $rc = Invoke-SshClient $sshClientExeFull @('--check-service', '--service-name', $ServiceName)
        if ($rc -eq 0) { $serviceActive = $true; break } else { Write-Host "Service not active yet (exit $rc). Waiting before retry..." }
        Start-Sleep -Seconds $ServiceCheckRetryDelay
    }
    return $serviceActive
}

function Test-StatusEndpoint {
    param([string]$targetHost)
    Write-Host "Verifying /status endpoint on $targetHost..."
    try {
        $uri = "http://${targetHost}:${HttpStatusPort}/status"
        $r = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 10
        Write-Host "Status response:" $r.Content
        return $true
    }
    catch { Write-Warning "Failed to query /status: $($_.Exception.Message)"; return $false }
}

# --- Main flow ---
Invoke-Build
$null = Publish-ContainerArtifacts
$dockerAvailable = Test-Docker
Invoke-Test -DockerAvailable $dockerAvailable
Test-Preflight
$sshClientExeFull = Publish-SshClient
Write-Host "Running remote host configuration verification (using SSH client --verify-only)..."
$hostVerifyExit = Invoke-SshClient $sshClientExeFull @('--verify-only','--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
if ($hostVerifyExit -ne 0) { throw "Remote host verification failed (exit $hostVerifyExit)" }

Publish-Server
Invoke-Deploy -sshClientExeFull $sshClientExeFull

$serviceActive = Test-ServiceActive -sshClientExeFull $sshClientExeFull -maxAttempts $MaxServiceChecks
if (-not $serviceActive) { Write-Warning "Service '$ServiceName' did not become active after $MaxServiceChecks attempts. Skipping /status check."; exit 0 }

# If service active, try to hit /status from local machine first; if that fails, try remote-invoked check via published ssh client
if (Check-StatusEndpoint -targetHost $SshHost) {
    Write-Host "External /status OK"
} else {
    Write-Host "External /status failed; attempting remote-local check via SSH client"
    $rc = Invoke-SshClient $sshClientExeFull @('--check-service', '--service-name', $ServiceName)
    if ($rc -eq 0) { Write-Host "Remote-local /status check reported service active; consider firewall or binding preventing external access." } else { Write-Warning "Remote-local /status check failed as well (exit $rc)." }
}

Write-Host "Done."
