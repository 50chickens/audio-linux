function Invoke-DotNetCoreDownload($netcoreMajorVersion) {
    ## query the latest .NET Core  SDK download URL for Linux ARM64 from the github releases api and download it locally 
    $releases = "https://api.github.com/repos/dotnet/core-sdk/releases"

    Write-Host "[dotnet] Querying .NET Core releases from $releases ..."
    $response = Invoke-RestMethod -Uri "https://api.github.com/repos/dotnet/core-sdk/releases" -UseBasicParsing
    write-host $response[0].tag_name
}

function Install-DotNetCoreRemote ($folder)
{
    Write-Host "[dotnet] Installing .NET Core  on $RemoteUser@$RemoteHost using install-dotnet.sh..."
    $sshArgs = @((Get-SshArgs), "bash -c 'chmod +x ~/install-dotnet.sh && ~/install-dotnet.sh $ArchivePath $PwshVersion $debugFlag'") |%{$_}
    try {
        & ssh @sshArgs
        Write-Host ".NET Core  installed remotely using install-dotnet.sh."
    } catch {
        Write-Host "Remote .NET Core install failed: $_"
        throw
    }
}

function Invoke-NetcoreUninstallationOnRemoteHost ($folder) 
{
    $sshArgs = @()
    $sshArgs = @((Get-SshArgs), "bash ./remove-dotnet.sh") |%{$_}
    Write-Verbose "sshArgs = $sshArgs"
    try {
        & ssh @sshArgs
        Write-Host ".NET Core removed from remote host using remove-dotnet.sh."
    } catch {
        Write-Host "Remote .NET Core uninstall failed: $_"
        throw
    }
}
function Install-NetCoreOrchestrated {
    param(
        [string]$PSScriptRoot,
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey,
        [bool]$DownloadOnRemoteHost,
        [switch]$Debug,
        [switch]$InstallAll,
        [switch]$UninstallNetCore
    )
    $latestNetCoreTag = "9.0.306"
    $netcoreArchiveUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$latestNetCoreTag/dotnet-sdk-$latestNetCoreTag-linux-arm64.tar.gz"
    $netcoreArchiveName = "dotnet-sdk-linux-arm64.tar.gz"
    $netcoreArchiveLocal = "$PSScriptRoot/downloads/$netcoreArchiveName"

    if (-not $DownloadOnRemoteHost) {
        if (-not (Test-Path $netcoreArchiveLocal)) {
            Invoke-WebRequest -Uri $netcoreArchiveUrl -OutFile $netcoreArchiveLocal
            if (-not (Test-Path $netcoreArchiveLocal)) {
                throw "[dotnet] Failed to download $netcoreArchiveUrl to $netcoreArchiveLocal"
            }
        }
    }

    if (-not $DownloadOnRemoteHost) {
        $sshArgsClean = Get-SshArgs $RemoteUser $RemoteHost $SshKey
        $sshArgsClean += "bash ./clean-tmp.sh"
        & ssh @sshArgsClean
    }

    $sshArgs = Get-SshArgs $RemoteUser $RemoteHost $SshKey
    $sshArgs += "bash ./check-diskspace.sh /tmp"
    $dfOutput = (& ssh @sshArgs) -replace '[^\d]', ''
    $tmpFreeMB = 0
    if ($dfOutput -match "^\d+$") {
        $tmpFreeMB = [long]$dfOutput
    }
    $tmpFreeGB = [math]::Round($tmpFreeMB / 1024, 2)
    if ($tmpFreeGB -lt 1) {
        Write-Host "[dotnet] /tmp has less than 1GB free ($tmpFreeGB GB), using home directory for upload."
        $netcoreArchiveRemote = "/home/$RemoteUser/$netcoreArchiveName"
    } else {
        $netcoreArchiveRemote = "/tmp/$netcoreArchiveName"
        # Run install-dotnet.sh using consistent bash script execution technique
        $sshArgsInstall = @((Get-SshArgs $RemoteUser $RemoteHost $SshKey), "bash -c 'chmod +x ~/install-dotnet.sh && ~/install-dotnet.sh $netcoreArchiveRemote $latestNetCoreTag DownloadOnRemoteHost'") |%{$_ | Convert-LineEndingsToWindows}
        Write-Host "sshArgs = $sshArgsInstall"
        $output = & ssh @sshArgsInstall
        # Ensure output is split into lines before conversion
        if ($output -is [string]) {
            $outputLines = $output -split "`n"
        } else {
            $outputLines = $output
        }
        Write-Host (Format-SshOutput ($output | Out-String))
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[dotnet] Remote install failed with exit code $LASTEXITCODE"
            exit 1
        }

        # Do not run dotnet hello world during netcore install
    }

    if ($UninstallNetCore) {
        Write-Host "Skipping hello world test (uninstall mode)."
        exit 0
    }
    # Remove hello world test from installation phase
}


function Test-NetCoreHelloWorld {
    param(
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$SshKey
    )
    $sshArgsTest = @((Get-SshArgs $RemoteUser $RemoteHost $SshKey), "bash -c 'chmod +x ~/test-dotnet.sh && ~/test-dotnet.sh ~/hello-dotnet.cs'") |%{$_}
    try {
        $output = & ssh @sshArgsTest
        $exitCode = $LASTEXITCODE
        Write-Host "Remote .NET Core HelloWorld output:"
        Write-Host (Format-SshOutput ($output | Out-String))
        if ($exitCode -eq 0) {
            Write-Host ".NET Core HelloWorld test passed (exit code 0)."
        } else {
            Write-Host ".NET Core HelloWorld test failed (exit code $exitCode)."
            exit 1
        }
    } catch {
        Write-Host "Remote .NET Core HelloWorld test failed: $_"
        exit 1
    }
}