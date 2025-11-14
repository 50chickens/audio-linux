#!/usr/bin/env bash
set -euo pipefail

# Start the Asionyx.Service.Systemd emulator in background (if present)
if [ -x "/app/systemd/Asionyx.Service.Systemd" ] || [ -f "/app/systemd/Asionyx.Service.Systemd.dll" ]; then
  echo "Starting Asionyx.Service.Systemd emulator..."
  if [ -x "/app/systemd/Asionyx.Service.Systemd" ]; then
    /app/systemd/Asionyx.Service.Systemd &
  else
    dotnet /app/systemd/Asionyx.Service.Systemd.dll &
  fi
  sleep 0.5
fi

echo "Starting deployment service from /app/deployment (auto-detect) ..."
# Find a dll in /app/deployment and run it with dotnet if present, otherwise try an executable
dep_dll=$(ls /app/deployment/*.dll 2>/dev/null | head -n1 || true)
dep_exe=$(ls /app/deployment/* 2>/dev/null | egrep -v '\.dll$' | head -n1 || true)
if [ -n "$dep_dll" ]; then
  exec dotnet "$dep_dll"
fi

# Prefer a published DLL and run it with dotnet. If no DLL is present, look for
# a true executable file (has execute permission) but explicitly exclude
# common runtime metadata files which may exist in the publish folder
# (e.g. .deps.json, .runtimeconfig.json, .pdb, .json metadata).
dep_exe=$(find /app/deployment -maxdepth 1 -type f -perm /111 \
  ! -name '*.dll' \
  ! -name '*.deps.json' \
  ! -name '*.runtimeconfig.json' \
  ! -name '*.pdb' \
  ! -name '*.json' -print | head -n1 || true)
if [ -n "$dep_exe" ]; then
  exec "$dep_exe"
fi

if [ -n "$dep_dll" ]; then
  # Fallback: if we somehow lost the earlier branch, run the DLL via dotnet
  exec dotnet "$dep_dll"
else
  echo "Deployment app not found in /app/deployment" >&2
  exec sleep infinity
fi
