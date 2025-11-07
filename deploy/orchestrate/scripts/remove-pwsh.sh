#!/bin/bash
# Remove PowerShell 7 and its PATH entry from the remote host

set -e

POWERSHELL_DIR="/opt/microsoft/powershell/7"
PWSH_SYMLINK="/usr/bin/pwsh"

if [ -d "$POWERSHELL_DIR" ]; then
    sudo rm -rf "$POWERSHELL_DIR"
    echo "Removed directory: $POWERSHELL_DIR"
else
    echo "Directory not found: $POWERSHELL_DIR"
fi

if [ -L "$PWSH_SYMLINK" ] || [ -f "$PWSH_SYMLINK" ]; then
    sudo rm -f "$PWSH_SYMLINK"
    echo "Removed symlink/file: $PWSH_SYMLINK"
else
    echo "Symlink/file not found: $PWSH_SYMLINK. Ignoring"
fi

echo "PowerShell removal process completed."