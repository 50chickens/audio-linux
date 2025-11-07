function Get-RemoteUploadDestination {
    param(
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey,
        [string]$ArchiveName,
        [int]$MinSpaceGB = 1
    )
    $checkSpaceCmd = "df --output=avail /tmp | tail -1"
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += $checkSpaceCmd
    $remoteSpace = & ssh @sshArgs
    $remoteSpaceGB = [long]($remoteSpace.Trim()) / (1024*1024)
    if ($remoteSpaceGB -lt $MinSpaceGB) {
        Write-Host "Not enough space in remote /tmp, uploading to home directory."
        return "~/$ArchiveName"
    } else {
        Write-Host "Uploading archive to remote /tmp."
        return "/tmp/$ArchiveName"
    }
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
    $remoteChecksumCmd = "sha256sum $RemotePath 2>/dev/null | cut -d' ' -f1"
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $remoteChecksum = (& ssh @sshArgs $remoteChecksumCmd) -replace '\r','' -replace '\n',''
    Write-Host "Local archive SHA256: $localChecksum"
    Write-Host ("Remote archive SHA256: " + (($remoteChecksum -split ' ')[0]))
    if ($remoteChecksum) {
        $remoteCrc = (($remoteChecksum -split ' ')[0]).ToUpper()
    } else {
        $remoteCrc = ""
    }
    $localCrc = $localChecksum.ToUpper()
    Write-Host "Local CRC: $localCrc"
    Write-Host "Remote CRC: $remoteCrc"
    if ($remoteChecksum -and $remoteCrc -eq $localCrc) {
        Write-Host "Checksums match (case insensitive). Skipping upload."
        return $false
    } else {
        Write-Host "Remote file missing or checksums do not match. Uploading archive to remote host..."
        $scpArgs = Get-ScpArgs $LocalPath "${RemoteUser}@${RemoteHost}:$RemotePath"
        & scp @scpArgs
        return $true
    }
}

#this function gets the pipeline input and replace linux line endings with windows line endings
function Convert-LineEndingsToWindows {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true, Position = 0)]
        [string]$InputObject
    )
    process {
        if ($null -eq $InputObject) { return }
        # Replace lone LF (not preceded by CR) with CRLF; preserve existing CRLF
        $Output = $InputObject -replace '(?<!\r)\n', "`r`n"
        Write-Output $Output
    }
}
function Format-SshOutput {
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string]$InputObject
    )
    process {
        if ($null -eq $InputObject) { return }
        if ($InputObject -is [string]) {
            $lines = $InputObject -split "`n"
        } else {
            $lines = $InputObject
        }
        $lines | Convert-LineEndingsToWindows
    }
}