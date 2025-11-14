#!/usr/bin/env bash
# Minimal shim: translate a few common systemctl commands to HTTP calls against localhost:5200
set -euo pipefail

HOST="http://127.0.0.1:5200"

if [ "$#" -lt 1 ]; then
  echo "usage: systemctl <cmd> [args...]"
  exit 2
fi

cmd="$1"; shift
case "$cmd" in
  daemon-reload)
    curl -s -X POST "$HOST/daemon-reload" || exit 1
    ;;
  enable)
    # enable just leaves the unit file in /etc/systemd/system; noop for shim
    exit 0
    ;;
  start)
    name="$1"
    curl -s -X POST "$HOST/unit/${name}/start" || exit 1
    ;;
  stop)
    name="$1"
    curl -s -X POST "$HOST/unit/${name}/stop" || exit 1
    ;;
  restart)
    name="$1"
    curl -s -X POST "$HOST/unit/${name}/restart" || exit 1
    ;;
  status)
    name="$1"
    curl -s "$HOST/unit/${name}/status" || exit 1
    ;;
  *)
    echo "unsupported systemctl command: $cmd"
    exit 2
    ;;
esac
