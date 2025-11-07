#!/bin/bash
set -e
SDK_TAR="$1"
SDK_VER="${2:-9.0.306}"
LOCALDOWNLOAD="${3:-}"

INSTALL_DIR="/opt/dotnet"
SDK_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/${SDK_VER}/dotnet-sdk-${SDK_VER}-linux-arm64.tar.gz"
echo "LOCALDOWNLOAD is set to '$LOCALDOWNLOAD'"

if [ "$LOCALDOWNLOAD" = "DownloadOnRemoteHost" ]; then
    echo "[dotnet] DownloadOnRemoteHost set. Downloading and installing on remote host."
    SDK_TAR="/tmp/dotnet-sdk-${SDK_VER}-linux-arm64.tar.gz"
    echo "Downloading .NET SDK from $SDK_URL to $SDK_TAR"
    wget --show-progress --progress=bar:force -O "$SDK_TAR" "$SDK_URL"
    sudo mkdir -p $INSTALL_DIR
    sudo tar -xzf $SDK_TAR -C $INSTALL_DIR
    DOTNET_BIN=$(find $INSTALL_DIR -type f -name dotnet | head -n 1)
    if [ -z "$DOTNET_BIN" ]; then
        echo "[dotnet] ERROR: dotnet binary not found after extraction."
        exit 1
    fi
    sudo ln -sf "$DOTNET_BIN" /usr/bin/dotnet
    sudo chmod +x /usr/bin/dotnet
    if ! [ -e /usr/bin/dotnet ]; then
        echo "[dotnet] ERROR: /usr/bin/dotnet symlink was not created."
        exit 1
    fi
    exit 0
fi

sudo mkdir -p $INSTALL_DIR
sudo tar -xzf $SDK_TAR -C $INSTALL_DIR
DOTNET_BIN=$(find $INSTALL_DIR -type f -name dotnet | head -n 1)
if [ -z "$DOTNET_BIN" ]; then
    echo "[dotnet] ERROR: dotnet binary not found after extraction."
    exit 1
fi
sudo ln -sf "$DOTNET_BIN" /usr/bin/dotnet
sudo chmod +x /usr/bin/dotnet
if ! [ -e /usr/bin/dotnet ]; then
    echo "[dotnet] ERROR: /usr/bin/dotnet symlink was not created."
    exit 1
fi