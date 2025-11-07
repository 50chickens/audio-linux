#!/bin/bash

DOTNET_DIR="/opt/microsoft/dotnet"

if [ -d "$DOTNET_DIR" ]; then
    if sudo rm -rf "$DOTNET_DIR"; then
        echo "[dotnet] Directory $DOTNET_DIR removed successfully."
    else
        echo "[dotnet] Error: Failed to remove $DOTNET_DIR." >&2
        exit 1
    fi
else
    echo "[dotnet] No .NET Core directory found at $DOTNET_DIR."
fi
SYMLINK_PATH="/usr/bin/dotnet"

if [ -L "$SYMLINK_PATH" ] || [ -e "$SYMLINK_PATH" ]; then
    if sudo rm -f "$SYMLINK_PATH"; then
        echo "[dotnet] Symlink or file $SYMLINK_PATH removed successfully."
    else
        echo "[dotnet] Error: Failed to remove symlink or file $SYMLINK_PATH." >&2
        exit 1
    fi
else
    echo "[dotnet] No symlink or file found at $SYMLINK_PATH."
fi