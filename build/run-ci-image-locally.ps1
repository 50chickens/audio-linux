param(
    [string]$ImageTag = 'audio-linux/ci-systemd-trixie:local',
    [string]$ContainerName = 'audio-ci-smoke',
    [int]$HostHttpPort = 5001,
    [int]$HostSshPort = 2222,
    [int]$TimeoutSeconds = 60
)

Write-Host "Building image $ImageTag from build/ci-systemd-trixie.Dockerfile..."
docker build -f build/ci-systemd-trixie.Dockerfile -t $ImageTag .

if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }

# Remove any existing container with same name
if (docker ps -a --format '{{.Names}}' | Select-String -Quiet -Pattern "^$ContainerName$") {
    Write-Host "Removing existing container $ContainerName..."
    docker rm -f $ContainerName | Out-Null
}

Write-Host "Running container $ContainerName (privileged, tmpfs /run, mount /sys/fs/cgroup)..."
docker run -d --privileged --tmpfs /run --tmpfs /run/lock -v /sys/fs/cgroup:/sys/fs/cgroup:ro -p ${HostHttpPort}:5001 -p ${HostSshPort}:22 --name $ContainerName $ImageTag

if ($LASTEXITCODE -ne 0) { throw "Docker run failed" }

Write-Host "Waiting for systemd to report running state (timeout: ${TimeoutSeconds}s)..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    try {
        $out = docker exec $ContainerName systemctl is-system-running 2>$null
        if ($out -and $out.Trim() -in @('running','degraded','maintenance')) {
            Write-Host "systemd state: $out"
            break
        }
    } catch {
        # ignore and retry
    }
    Start-Sleep -Seconds 2
}

if ((Get-Date) -ge $deadline) {
    Write-Warning "Timed out waiting for systemd to be running. Use 'docker logs $ContainerName' to inspect."
} else {
    Write-Host "Container is up. HTTP port mapped to localhost:$HostHttpPort"
}

Write-Host "To stop and remove the container: docker rm -f $ContainerName"
