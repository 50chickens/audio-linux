#!/bin/bash
set -e
if [ "$3" = "1" ]; then
    echo "Debug: install-pwsh.sh called at $(date)"
    echo "Debug: Arguments: ARCHIVE_PATH=\$1='$1', PwshVersion=\$2='$2', DebugFlag=\$3='$3'"
    set -x
fi

ARCHIVE_PATH="${1:-/tmp/powershell-linux-arm64.tar.gz}"
PWSH_VERSION="${2:-7.5.4}"
LOCALDOWNLOAD="${4:-}"

MIN_SPACE_MB=50
PWSH_VERSION_NUM="${PWSH_VERSION#v}"
PWSH_URL="https://github.com/PowerShell/PowerShell/releases/download/${PWSH_VERSION}/powershell-${PWSH_VERSION_NUM}-linux-arm64.tar.gz"

if [ "$LOCALDOWNLOAD" = "DownloadOnRemoteHost" ]; then
    echo "[pwsh] DownloadOnRemoteHost set, skipping remote download and exiting."
    exit 0
else
    ARCHIVE_PATH="/tmp/powershell-linux-arm64.tar.gz"
    echo "Downloading PowerShell from $PWSH_URL to $ARCHIVE_PATH"
    wget --show-progress --progress=bar:force -O "$ARCHIVE_PATH" "$PWSH_URL"
fi

# Always extract major version from $2 for install directory
MAJOR_VERSION=$(echo "$2" | grep -oP '\d+' | head -1)
if [ -z "$MAJOR_VERSION" ]; then
    MAJOR_VERSION="7"
    echo "Debug: Could not extract major version from PwshVersion='$2', defaulting to $MAJOR_VERSION"
fi
# Only use major version for install directory
POWERSHELL_DIR="/opt/microsoft/powershell/$MAJOR_VERSION"
POWERSHELL_BINARY="$POWERSHELL_DIR/pwsh"
PWSH_SYMLINK="/usr/bin/pwsh"
if [ "$3" = "1" ]; then
    echo "Debug: MAJOR_VERSION=$MAJOR_VERSION"
    echo "Debug: POWERSHELL_DIR=$POWERSHELL_DIR"
fi

# Check if PowerShell binary exists in target folder
if [ "$3" = "1" ]; then
    echo "Debug: Checking for PowerShell binary at $POWERSHELL_BINARY"
fi
if [ -f "$POWERSHELL_BINARY" ]; then
    if [ "$3" = "1" ]; then
        echo "PowerShell binary already exists at $POWERSHELL_BINARY, skipping extraction."
    fi
else
    if [ "$3" = "1" ]; then
        echo "Extracting PowerShell from $ARCHIVE_PATH..."
        echo "Debug: Disk space before extraction:"
        df -h
        echo "Debug: Memory before extraction:"
        free -h
        echo "Debug: Archive size:"
        ls -lh "$ARCHIVE_PATH"
        echo "Debug: Directory permissions for $POWERSHELL_DIR:"
        ls -ld "$POWERSHELL_DIR" || echo "Directory does not exist yet"
    fi
    sudo mkdir -p "$POWERSHELL_DIR"
    if [ "$3" = "1" ]; then
        echo "Debug: Directory permissions after mkdir:"
        ls -ld "$POWERSHELL_DIR"
        echo "Debug: Extracting archive..."
    fi
    sudo tar -xzf "$ARCHIVE_PATH" -C "$POWERSHELL_DIR" || { echo "Error: Extraction failed"; exit 2; }
    if [ "$3" = "1" ]; then
        echo "Debug: Extraction complete. Listing contents:"
        ls -l "$POWERSHELL_DIR"
    fi
    if [ ! -f "$POWERSHELL_BINARY" ]; then
        echo "Error: PowerShell binary not found after extraction at $POWERSHELL_BINARY"
        ls -l "$POWERSHELL_DIR"
        exit 127
    fi
    sudo chmod +x "$POWERSHELL_BINARY"
    if [ "$3" = "1" ]; then
        echo "Debug: chmod complete. Listing binary:"
        ls -l "$POWERSHELL_BINARY"
        echo "Debug: Checking shared library dependencies for $POWERSHELL_BINARY"
        ldd "$POWERSHELL_BINARY" || echo "ldd failed"
        ldd "$POWERSHELL_BINARY" | grep "not found" && echo "Warning: Missing dependencies for PowerShell binary"
    fi
fi
if [ "$3" = "1" ]; then
    echo "Before symlink:"
    ls -l "$POWERSHELL_BINARY" || echo "Binary not found"
    ls -l "$PWSH_SYMLINK" || echo "Symlink not found"
    echo "PATH: $PATH"
    echo "Debug: Listing contents of $POWERSHELL_DIR and /tmp"
    ls -l "$POWERSHELL_DIR"
    ls -l /tmp | grep powershell || echo "No powershell archive in /tmp"
fi
sudo rm -f "$PWSH_SYMLINK" 2>/dev/null
sudo ln -s "$POWERSHELL_BINARY" "$PWSH_SYMLINK" || { echo "Failed to create symlink $PWSH_SYMLINK"; exit 1; }
if [ "$3" = "1" ]; then
    echo "After symlink:"
    ls -l "$POWERSHELL_BINARY" || echo "Binary not found"
    ls -l "$PWSH_SYMLINK" || echo "Symlink not found"
    echo "PATH: $PATH"
fi


# test for permissions that allow us to execute pwsh and /opt/microsoft/powershell/7/pwsh
if [ "$3" = "1" ]; then
    echo "Debug: PWSH_SYMLINK=$PWSH_SYMLINK, POWERSHELL_BINARY=$POWERSHELL_BINARY"
    ls -l "$PWSH_SYMLINK" || echo "Symlink not found"
    ls -l "$POWERSHELL_BINARY" || echo "Binary not found"
fi
if [ -x "$PWSH_SYMLINK" ] && [ -x "$POWERSHELL_BINARY" ]; then
    echo "Symlink created and permissions are correct."
else
    echo "Error: Permissions are not set correctly for PowerShell." >&2
    exit 1
fi
# Run PowerShell and print version info, with error handling
if command -v pwsh >/dev/null 2>&1; then
    if [ "$3" = "1" ]; then
        echo "Checking available memory before running pwsh:"
        free -h
        mem_avail=$(awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo)
        echo "Available memory: ${mem_avail} MB"
        if [ "$mem_avail" -lt 200 ]; then
            echo "Warning: Less than 200MB RAM available. Skipping PowerShell hello world test."
            exit 0
        fi
    fi
    echo "Running: pwsh -c 'Write-Output \"Hello World from PowerShell\"'"
    pwsh -c 'Write-Output "Hello World from PowerShell"' || { echo "pwsh command failed"; exit 1; }
    if [ "$3" = "1" ]; then
        echo "Debug: pwsh exit code $?"
        echo "Debug: PATH after pwsh run: $PATH"
        echo "Debug: Listing /usr/bin and $POWERSHELL_DIR contents:"
        ls -l /usr/bin | grep pwsh || echo "pwsh not found in /usr/bin"
        ls -l "$POWERSHELL_DIR"
    fi
else
    echo "pwsh is not installed or not in PATH"
    exit 1
fi