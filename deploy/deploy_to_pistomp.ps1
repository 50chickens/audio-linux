param(
    [string]$RemoteHost = 'pistomp',
    [string]$User = 'pistomp',
    # Allow user to pass an explicit key file path. If not provided, we'll try to pick a key from the current user's .ssh folder
    [string]$KeyPath = '',
    [string]$RemoteDir = '/opt/audio-router',
    [switch]$DryRun,
    [switch]$SkipInstallNetCore,
    [switch]$NoOrchestrate
)

# Determine default ssh key path from the current user's .ssh folder if KeyPath not provided
$resolvedKey = $null
if (-not $KeyPath -or $KeyPath -eq '') {
    $userSshDir = Join-Path $env:USERPROFILE ".ssh"
    if (Test-Path $userSshDir) {
        # Preferred filenames in order
        $candidates = @('id_ed25519','id_rsa','id_ecdsa','pistomp','id_dsa')
        foreach ($c in $candidates) {
            $f = Join-Path $userSshDir $c
            if (Test-Path $f) { $resolvedKey = $f; break }
        }
        if (-not $resolvedKey) {
            # fallback: pick any private key file (non .pub)
            $files = Get-ChildItem -Path $userSshDir -File | Where-Object { $_.Name -notlike '*.pub' }
            if ($files.Count -gt 0) { $resolvedKey = $files[0].FullName }
        }
    }
} else {
    if (Test-Path $KeyPath) { $resolvedKey = $KeyPath }
}

if ($resolvedKey) { $KeyPath = $resolvedKey }

Write-Host "Assumption: private key is available at $KeyPath (not the .pub file). If you only have the .pub, provide the matching private key path." -ForegroundColor Yellow

$localPublish = Resolve-Path -Path "publish"
if (-not (Test-Path $localPublish)) {
    throw "Publish folder not found. Run build.ps1 first."
}

Write-Host "Preparing to deploy to $RemoteHost (user: $User). DryRun=$DryRun"

# Prepare scp/ssh identity argument (only include -i when a valid key path is provided)
$scpIdentityArg = ""
if ($KeyPath -and (Test-Path $KeyPath)) {
    $scpIdentityArg = "-i `"$KeyPath`""
} else {
    Write-Host "Note: SSH key path '$KeyPath' not found or empty; scp/ssh will run without -i (use ssh-agent or default keys)."
}

if (-not $NoOrchestrate) {
    # Step 1: Upload orchestrate scripts to remote host (so remote host has install scripts)
    $orchestrateLocal = Resolve-Path -Path "deploy/orchestrate"
    if (-not (Test-Path $orchestrateLocal)) {
        throw "Orchestrate folder not found at deploy/orchestrate."
    }

    Write-Host "Uploading orchestrate helper scripts to $RemoteHost..."
    if ($DryRun) {
        Write-Host ("DRYRUN: scp -i {0} -r {1}/* {2}@{3}:~/" -f $KeyPath, $orchestrateLocal, $User, $RemoteHost)
    } else {
        if ($scpIdentityArg) { scp $scpIdentityArg -r "$orchestrateLocal/*" ${User}@${RemoteHost}:~/ } else { scp -r "$orchestrateLocal/*" ${User}@${RemoteHost}:~/ }
    }

    # Step 2: Run orchestrator locally to upload scripts and optionally install .NET on remote
    $orchestrateScript = "deploy/orchestrate/deploy-and-run.ps1"
    if (-not (Test-Path $orchestrateScript)) {
        throw "Orchestrate bootstrap script not found: $orchestrateScript"
    }

    Write-Host "Invoking orchestrator to install .NET on remote host (using remote download by default)"
    $pwshCmd = "pwsh -NoProfile -ExecutionPolicy Bypass -File `"$orchestrateScript`" -RemoteUser $User -RemoteHost $RemoteHost -SshKey $KeyPath -UploadScripts -InstallNetCore -DownloadOnRemoteHost:`$true"
    if ($DryRun) {
        Write-Host "DRYRUN: $pwshCmd"
    } else {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $orchestrateScript -RemoteUser $User -RemoteHost $RemoteHost -SshKey $KeyPath -UploadScripts
        if (-not $SkipInstallNetCore) {
            & pwsh -NoProfile -ExecutionPolicy Bypass -File $orchestrateScript -RemoteUser $User -RemoteHost $RemoteHost -SshKey $KeyPath -InstallNetCore -DownloadOnRemoteHost:$true
        } else {
            Write-Host "Skipping remote .NET install as requested (SkipInstallNetCore=true)"
        }
    }
} else {
    Write-Host "Skipping any use of deploy/orchestrate as requested (-NoOrchestrate)"
}

# Step 3: Bootstrap and start the deployment service on the remote host
Write-Host "Publishing deployment service locally"
$deployServiceProject = "src/Asionyx.Tools.Deployment"
$deployServicePublishPath = Join-Path $deployServiceProject "bin/Release/net8.0/publish"
if (-not (Test-Path $deployServicePublishPath)) {
    Write-Host "Publishing Asionyx.Tools.Deployment to temporary folder..."
    if ($DryRun) {
        Write-Host "DRYRUN: dotnet publish $deployServiceProject -c Release -o publish/deployment-service"
        # pick the expected path for dry run
        $deployServicePublishPath = "publish/deployment-service"
    } else {
        dotnet publish $deployServiceProject -c Release -o publish/deployment-service
        $deployServicePublishPath = Resolve-Path -Path "publish/deployment-service"
    }
}

Write-Host "Deploying deployment service (bootstrap) to $RemoteHost"
$remoteDeployDir = "/opt/deployment-service"
    # Copy publish files to the remote user's home first, then use sudo on the remote to move into /opt (avoids scp permission issues)
    $remoteTempDir = "~/deployment-service"
    if ($DryRun) {
        Write-Host ("DRYRUN: scp -i {0} -r {1}/* {2}@{3}:{4}" -f $KeyPath, $deployServicePublishPath, $User, $RemoteHost, $remoteTempDir)
        Write-Host ("DRYRUN: scp -i {0} deploy/deployment-service.service {1}@{2}:/tmp/deployment-service.service" -f $KeyPath, $User, $RemoteHost)
        Write-Host ("DRYRUN: ssh -i {0} {1}@{2} 'sudo mkdir -p {3} && sudo rm -rf {3}/* && sudo mv {4}/* {3} && sudo mv /tmp/deployment-service.service /etc/systemd/system/deployment-service.service && sudo systemctl daemon-reload && sudo systemctl enable --now deployment-service'" -f $KeyPath, $User, $RemoteHost, $remoteDeployDir, $remoteTempDir)
    } else {
        if ($KeyPath -and (Test-Path $KeyPath)) {
            scp -i $KeyPath -r "$deployServicePublishPath/*" ${User}@${RemoteHost}:${remoteTempDir}
            scp -i $KeyPath "deploy/deployment-service.service" ${User}@${RemoteHost}:/tmp/deployment-service.service
            ssh -i $KeyPath ${User}@${RemoteHost} "sudo mkdir -p ${remoteDeployDir} && sudo rm -rf ${remoteDeployDir}/* && sudo mv ${remoteTempDir}/* ${remoteDeployDir} && sudo mv /tmp/deployment-service.service /etc/systemd/system/deployment-service.service && sudo systemctl daemon-reload && sudo systemctl enable --now deployment-service"
        } else {
            scp -r "$deployServicePublishPath/*" ${User}@${RemoteHost}:${remoteTempDir}
            scp "deploy/deployment-service.service" ${User}@${RemoteHost}:/tmp/deployment-service.service
            ssh ${User}@${RemoteHost} "sudo mkdir -p ${remoteDeployDir} && sudo rm -rf ${remoteDeployDir}/* && sudo mv ${remoteTempDir}/* ${remoteDeployDir} && sudo mv /tmp/deployment-service.service /etc/systemd/system/deployment-service.service && sudo systemctl daemon-reload && sudo systemctl enable --now deployment-service"
        }
    }

Write-Host "Waiting for deployment service to become available on http://${RemoteHost}:5001/health"
$apiKey = $env:DEPLOY_API_KEY; if (-not $apiKey) { $apiKey = 'changeme' }
$health= $null; $tries = 0
while ($tries -lt 30) {
    if ($DryRun) { Write-Host "DRYRUN: Invoke-RestMethod -Uri http://${RemoteHost}:5001/health -Headers @{ 'X-Api-Key' = '$apiKey' } -Method GET"; break }
    try {
        $resp = Invoke-RestMethod -Uri "http://${RemoteHost}:5001/health" -Method Get -Headers @{ 'X-Api-Key' = $apiKey }
    if ($null -ne $resp) { $health = $resp; break }
    } catch {
        Start-Sleep -Seconds 2
    }
    $tries++
}

if (-not $health -and -not $DryRun) { throw "Deployment service did not become healthy in time" }

Write-Host "Uploading audio-router via deployment service HTTP API"
# Create a zip of the published audio-router files and upload
$zipPath = Join-Path $env:TEMP "audio-router-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
if ($DryRun) {
    Write-Host "DRYRUN: Compress-Archive -Path $localPublish/* -DestinationPath $zipPath -Force"
} else {
    Compress-Archive -Path "$localPublish/*" -DestinationPath $zipPath -Force
}

Write-Host "Preparing multipart/form-data upload for audio-router zip (metadata + file)"
if ($DryRun) {
    Write-Host ("DRYRUN: POST multipart/form-data to http://{0}:5001/api/files/upload with form fields: metadata (JSON) and file (binary)" -f $RemoteHost)
} else {
    $uploadUri = "http://${RemoteHost}:5001/api/files/upload"
    $meta = @{ TargetDir = $RemoteDir; FileName = [System.IO.Path]::GetFileName($zipPath) } | ConvertTo-Json -Compress
    # Use -Form with a hashtable where file is provided as a file info via Get-Item
    $form = @{
        metadata = $meta
        file = Get-Item $zipPath
    }
    Invoke-RestMethod -Uri $uploadUri -Method Post -Headers @{ 'X-Api-Key' = $apiKey } -Form $form
}

Write-Host "Installing and enabling audio-router systemd unit via deployment service"
$unitContent = Get-Content -Raw "deploy/audio-router.service"
$installUri = "http://${RemoteHost}:5001/api/services/install"
if ($DryRun) {
    Write-Host "DRYRUN: Would POST JSON to $installUri with X-Api-Key header and body { Name = \"audio-router\", UnitFileContent = <unit content> }"
} else {
    Invoke-RestMethod -Uri $installUri -Method Post -Headers @{ "X-Api-Key" = $apiKey } -Body (ConvertTo-Json @{ Name = "audio-router"; UnitFileContent = $unitContent }) -ContentType "application/json"
}

Write-Host "Deployment of audio-router via deployment service complete. Check service status via deployment API or ssh to verify." 
