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
    if (-not $remoteSpace -or -not ($remoteSpace.Trim() -match '^\d+$')) {
        Write-Host "Could not determine remote /tmp space, defaulting to home directory."
        return "~/$ArchiveName"
    }
    $remoteSpaceGB = [int]($remoteSpace.Trim()) / (1024*1024)
    if ($remoteSpaceGB -lt $MinSpaceGB) {
        Write-Host "Not enough space in remote /tmp, uploading to home directory."
        return "~/$ArchiveName"
    } else {
        Write-Host "Uploading PowerShell archive to remote /tmp."
        return "/tmp/$ArchiveName"
    }
}