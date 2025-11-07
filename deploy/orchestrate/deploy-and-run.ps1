# PowerShell orchestration script for remote PowerShell and .NET Core install/management on Raspberry Pi

param(
    [string]$RemoteUser = "pistomp",
    [string]$RemoteHost = "pistomp",
    [string]$SshKey = "$env:USERPROFILE\.ssh\pistomp-privatekey",
    [switch]$UninstallPowerShell,
    [switch]$UninstallNetCore,
    [switch]$UninstallAll,
    [switch]$InstallPowerShell,
    [switch]$InstallNetCore,
    [switch]$Debug,
    [switch]$InspectHost,
    [bool]$DownloadOnRemoteHost = $true, ##if true, remote host downloads installation files directly from internet, othwise files are uploaded from local machine
    [switch]$TestPowerShellHelloWorld,
    [switch]$TestNetCoreHelloWorld,
    [switch]$UploadScripts,
    [switch]$DisableIPv6
)
if ($DisableIPv6) {
    Write-Host "Uploading disable-ipv6.sh to remote host..."
    $scpArgs = Get-ScpArgs "$PSScriptRoot/scripts/disable-ipv6.sh" "${RemoteUser}@${RemoteHost}:~/disable-ipv6.sh"
    & scp @scpArgs
    Write-Host "Disabling IPv6 on remote host..."
    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += "bash ~/disable-ipv6.sh"
    $output = & ssh @sshArgs 2>&1
    Write-Host $output
    exit 0
}


$errorpreference = "stop"
get-childitem "$PSScriptRoot/includes" -Filter "*.ps1" | ForEach-Object {
    Write-Verbose "Sourcing $($_.FullName)..."
    . $_.FullName
}

# Set debug flag for downstream scripts
if ($Debug) {
    $env:PISTOMP_DEBUG = "1"
} else {
    $env:PISTOMP_DEBUG = "0"
}

if ($UploadScripts) 
{
    Write-Host "Uploading helper scripts to remote host..."
    get-childitem "$PSScriptRoot/scripts" -Filter "*.*" | ForEach-Object {
        #uploading file to remote host via scp
        $scpArgs = Get-ScpArgs $_.FullName "${RemoteUser}@${RemoteHost}:~/$($_.Name)"
        Write-Host "Uploading $($_.Name) to remote host..."
        & scp @scpArgs
    }
    exit
}

Set-DefaultSshArguments -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey

if ($InspectHost) {
    Write-Host "Inspecting remote host for basic requirements..."

    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += "bash ~/inspect-host.sh"
    $output = & ssh @sshArgs 2>&1
    Write-Host $output
    if ($output -match "Warning: Both /tmp and home have less than 100MB free.") {
        exit 1
    }
    Write-Host "Disk space requirements met."
    exit 0
}


if ($UninstallPowerShell -or $UninstallAll)
{
    Write-Host "Uninstalling PowerShell from remote host..."
    Invoke-PwshUninstallationOnRemoteHost -ScriptPath "~/remove-pwsh.sh"
    # Do not run hello world test after uninstall
    exit 0
}
if ($UninstallNetCore -or $UninstallAll)
{
    Invoke-NetcoreUninstallationOnRemoteHost
    # Do not run hello world test after uninstall
    exit 0
}

if ($InstallPowerShell -or $InstallAll)
{
    Install-PowerShellOrchestrated -PSScriptRoot $PSScriptRoot -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey -DownloadOnRemoteHost $DownloadOnRemoteHost -Debug:$Debug -InstallAll:$InstallAll
    if ($TestPowerShellHelloWorld) {
        Test-PowerShellHelloWorld -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey
    }
}

if ($InstallNetCore -or $InstallAll)
{
    Install-NetCoreOrchestrated -PSScriptRoot $PSScriptRoot -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey -DownloadOnRemoteHost $DownloadOnRemoteHost -Debug:$Debug -InstallAll:$InstallAll
    if ($TestNetCoreHelloWorld) {
        Test-NetCoreHelloWorld -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey
    }
}

if ($TestPowerShellHelloWorld) 
{
    Test-PowerShellHelloWorld -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey
}

if ($TestNetCoreHelloWorld) 
{
    Test-NetCoreHelloWorld -RemoteUser $RemoteUser -RemoteHost $RemoteHost -SshKey $SshKey
}