<#
Helper functions moved from root orchestrator. These functions do NOT call the local ssh.exe.
They delegate remote work to the published Asionyx.Tools.Deployment.Client.Ssh binary.
#>

function Initialize-RemoteDirectory {
    param(
        [string]$SshClientExeFull,
        [string]$SshHost,
        [string]$SshUser,
        [string]$SshKey,
        [int]$SshPort = 22
    )
    Write-Host "Delegating remote deploy-directory creation to SSH client on $SshHost..."
    $argList = @('--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
    $out = & "$SshClientExeFull" $argList 2>&1
    $rc = $LASTEXITCODE
    if ($out) { Write-Host $out }
    if ($rc -ne 0) { throw "SSH client failed whilst ensuring remote directory (exit $rc)" }
}

function Initialize-UserDataDirectoryRemote {
    param(
        [string]$SshClientExeFull,
        [string]$User,
        [string]$SshHost,
        [string]$SshUser,
        [string]$SshKey,
        [int]$SshPort = 22
    )
    Write-Host "Delegating user-data directory creation for user '$User' on $SshHost to SSH client..."
    $argList = @('--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
    $out = & "$SshClientExeFull" $argList 2>&1
    $rc = $LASTEXITCODE
    if ($out) { Write-Host $out }
    if ($rc -ne 0) { throw "SSH client failed whilst ensuring user data directory (exit $rc)" }
}

function Install-SystemdUnit {
    param(
        [string]$SshClientExeFull,
        [string]$SshHost,
        [string]$SshUser,
        [string]$SshKey,
        [int]$SshPort = 22,
        [string]$ServiceName = 'deployment-service',
        [string]$RemoteDeployDir = '/opt/Asionyx.Service.Deployment.Linux'
    )
    Write-Host "Delegating systemd install/enable/start of $ServiceName on $SshHost to SSH client..."
    $argList = @('--ssh-host',$SshHost,'--ssh-user',$SshUser,'--ssh-key',$SshKey,'--ssh-port',$SshPort)
    $out = & "$SshClientExeFull" $argList 2>&1
    $rc = $LASTEXITCODE
    if ($out) { Write-Host $out }
    if ($rc -ne 0) { throw "SSH client failed whilst installing systemd unit (exit $rc)" }
}

Export-ModuleMember -Function Initialize-RemoteDirectory,Initialize-UserDataDirectoryRemote,Install-SystemdUnit
