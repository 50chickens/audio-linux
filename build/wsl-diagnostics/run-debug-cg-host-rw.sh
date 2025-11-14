#!/usr/bin/env bash
set -euo pipefail

# Try mounting /sys/fs/cgroup as read-write instead of ro and run systemd in foreground

docker run --rm \
  --privileged \
  --cgroupns=host \
  --security-opt seccomp=unconfined \
  --tmpfs /run \
  --tmpfs /run/lock \
  -v /sys/fs/cgroup:/sys/fs/cgroup:rw \
  audio-linux/ci-systemd-trixie:local /lib/systemd/systemd --log-level=debug 2>&1 | sed -n '1,400p'
