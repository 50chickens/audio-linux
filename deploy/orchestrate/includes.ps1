
function Install-DotNetRemote {
    # Use official .NET install script from Microsoft
     "Invoke-WebRequest -Uri 'https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1' -OutFile '/tmp/dotnet-install.ps1'"
     "pwsh -File /tmp/dotnet-install.ps1 -Channel LTS -Architecture arm64"
     "\$HOME/.dotnet/dotnet --info"
}
