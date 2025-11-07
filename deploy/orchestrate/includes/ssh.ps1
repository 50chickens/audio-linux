function Set-DefaultSshArguments {
    param(
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey
    )
    write-host "Setting default SSH arguments: User=$RemoteUser, Host=$RemoteHost, SshKey=$SshKey"
    $script:RemoteUser = $RemoteUser
    $script:RemoteHost = $RemoteHost
    $script:SshKey = $SshKey
}
function Get-SshArgs()
{
    Write-Verbose "Getting SSH args for $RemoteUser@$RemoteHost with key $SshKey"
    return @("-o", "StrictHostKeyChecking=no", "-i", $script:SshKey, "$($script:RemoteUser)@$($script:RemoteHost)")
}

function Get-ScpArgs($LocalFile, $RemoteFile) {
    Write-Verbose "Get-ScpArgs: LocalFile=$LocalFile, RemoteFile=$RemoteFile"
    return @($LocalFile, $RemoteFile)
}