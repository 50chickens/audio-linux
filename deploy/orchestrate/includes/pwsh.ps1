function Get-LatestPwshReleaseInfo {
    param(
        [string]$Arch = "linux-arm64"
    )
    $releaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/PowerShell/PowerShell/releases/latest"
    $latestTag = $releaseInfo.tag_name
    Write-Host "Latest PowerShell release tag: $latestTag"
    
    $pwshArchiveName = "powershell-$Arch.tar.gz"
    $pwshArchiveUrl = "https://github.com/PowerShell/PowerShell/releases/download/$latestTag/$pwshArchiveName"
    return @{ Tag = $latestTag; ArchiveName = $pwshArchiveName; ArchiveUrl = $pwshArchiveUrl }
}

function Download-PwshArchiveIfNeeded {
    param(
        [string]$DownloadDir,
        [string]$ArchiveName,
        [string]$ArchiveUrl,
        [bool]$DownloadOnRemoteHost
    )
    $pwshArchiveLocal = Join-Path $DownloadDir $ArchiveName
    if (-not $DownloadOnRemoteHost) {
        if (-not (Test-Path $pwshArchiveLocal)) {
            Write-Host "Downloading PowerShell archive from $ArchiveUrl to $pwshArchiveLocal..."
            Invoke-WebRequest -Uri $ArchiveUrl -OutFile $pwshArchiveLocal
        } else {
            Write-Host "PowerShell archive already exists locally."
        }
    }
    return $pwshArchiveLocal
}

function Test-RemotePwshHelloWorld()
{
   $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
   $sshArgs += "pwsh -c Write-Output 'HelloWorld from PowerShell'"
   try
   {
       $output = & ssh @sshArgs 2>&1
       Write-Host "Remote PowerShell helloworld output:"
       Write-Host (Format-SshOutput $output)
   } catch
   {
       Write-Host "Remote helloworld test failed: $_"
       exit 1
   }
}


# Upload and execute remove-pwsh.sh on remote host
function Invoke-PwshUninstallationOnRemoteHost {
    param(
        [string]$ScriptPath = "./remove-pwsh.sh"
    )
    $sshArgs = @()
    $sshArgs = @((Get-SshArgs), "bash $ScriptPath") |%{$_}
    Write-Host "sshArgs = $sshArgs"
    & ssh @sshArgs
    return
}

function Invoke-PwshTestOnRemoteHost ()
{
    Write-Host "Checking for PowerShell on $RemoteUser@$RemoteHost..."
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += "pwsh --version"
    try {
        $output = & ssh @sshArgs 2>&1
        $output = Format-SshOutput $output
        if ($output -match "^PowerShell") {
            Write-Host "PowerShell is available: $output"
            Write-Host "pwsh found at: /opt/microsoft/powershell/7/pwsh"
            return $true
        } else {
            Write-Host "PowerShell not found. Output: $output"
            return $false
        }
    } catch {
        Write-Host "SSH connection failed: $_"
        return $false
    }
}

function Invoke-PwshInstallationOnRemoteHost {
    param(
        [string]$ArchivePath = "/tmp/powershell-latest-linux-arm64.tar.gz",
        [string]$PwshVersion = "latest"
    )
    $debugFlag = ""
    if ($env:PISTOMP_DEBUG -eq "1") {
        $debugFlag = "1"
    }
    $sshArgs = @()
    $sshArgs = @((Get-SshArgs), "bash -c 'chmod +x ~/install-pwsh.sh && ~/install-pwsh.sh $ArchivePath $PwshVersion $debugFlag'") |%{$_}
    Write-Host "sshArgs = $sshArgs"
    & ssh @sshArgs
    return
}

function UploadArchiveIfNeeded {
    param(
        [string]$LocalPath,
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey,
        [string]$RemotePath
    )
    $localChecksum = (Get-FileHash $LocalPath -Algorithm SHA256).Hash
    $remoteChecksumCmd = "sha256sum $RemotePath 2>/dev/null | awk '{print \$1}'"
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $remoteChecksum = (& ssh @sshArgs $remoteChecksumCmd) -replace '\r','' -replace '\n',''
    Write-Host "Local archive SHA256: $localChecksum"
    Write-Host ("Remote archive SHA256: " + (($remoteChecksum -split ' ')[0]))
    if ($null -eq $remoteChecksum -or $remoteChecksum -eq "") {
        $remoteCrc = ""
    } else {
        $remoteCrc = (($remoteChecksum -split ' ')[0]).ToUpper()
    }
    $localCrc = $localChecksum.ToUpper()
    Write-Host "Local CRC: $localCrc"
    Write-Host "Remote CRC: $remoteCrc"
    if ($remoteCrc -and $remoteCrc -eq $localCrc) {
        Write-Host "Checksums match (case insensitive). Skipping upload."
        return $false
    } else {
        Write-Host "Checksums do not match or remote file missing. Uploading archive to remote host..."
        return $true
    }
}
function Install-PowerShellOrchestrated {
    param(
        [string]$PSScriptRoot,
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey,
        [bool]$DownloadOnRemoteHost,
        [switch]$Debug,
        [switch]$InstallAll,
        [switch]$UninstallPwsh
    )

    # Get latest PowerShell release info
    $pwshReleaseInfo = Get-LatestPwshReleaseInfo -Arch "linux-arm64"
    $latestTag = $pwshReleaseInfo.Tag
    $pwshArchiveName = $pwshReleaseInfo.ArchiveName
    $pwshArchiveUrl = $pwshReleaseInfo.ArchiveUrl
    $pwshArchiveRemote = Get-RemoteUploadDestination -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey -ArchiveName $pwshArchiveName -MinSpaceMB 50

    if ($DownloadOnRemoteHost)
    {
        Invoke-PwshInstallationOnRemoteHost -ArchivePath $pwshArchiveRemote -PwshVersion $latestTag -ExtraArgs "DownloadOnRemoteHost"
    }
    else
    {
        $pwshArchiveLocal = Download-PwshArchiveIfNeeded -DownloadDir "$PSScriptRoot/downloads" -ArchiveName $pwshArchiveName -ArchiveUrl $pwshArchiveUrl -DownloadOnRemoteHost:$DownloadOnRemoteHost
        Copy-Item -Path $pwshArchiveLocal -Destination "$PSScriptRoot/downloads/powershell-linux-arm64.tar.gz" -Force
        $didUpload = UploadArchiveIfNeeded `
            -LocalPath "$PSScriptRoot/downloads/powershell-linux-arm64.tar.gz" `
            -RemoteUser $RemoteUser `
            -RemoteHost $RemoteHost `
            -SshKey $SshKey `
            -RemotePath $pwshArchiveRemote
        Invoke-PwshInstallationOnRemoteHost -ArchivePath $pwshArchiveRemote -PwshVersion $latestTag
    }

    if ($UninstallPwsh) {
        Write-Host "Skipping helloworld test (uninstall mode)."
        exit 0
    }
    # Remove helloworld test from installation phase
}

function Test-PowerShellHelloWorld {
    param(
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey
    )
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += 'pwsh -c "Write-Output \"Hello World from PowerShell\""'
    try {
        $output = & ssh @sshArgs 2>&1
        $exitCode = $LASTEXITCODE
        Write-Host "Remote PowerShell HelloWorld output:"
        Write-Host (Format-SshOutput $output)
        if ($exitCode -eq 0) {
            Write-Host "PowerShell HelloWorld test passed (exit code 0)."
        } else {
            Write-Host "PowerShell HelloWorld test failed (exit code $exitCode)."
            exit 1
        }
    } catch {
        Write-Host "Remote PowerShell HelloWorld test failed: $_"
        exit 1
    }
}
