#!/usr/bin/env bash
set -euo pipefail

# Run the image in foreground to capture systemd stdout/stderr directly with debug log level.
# This will run until systemd exits (which historically happens quickly for this image),
# and will print up to the first 400 lines of combined output.

docker run --rm \
  --privileged \
  --cgroupns=host \
  --security-opt seccomp=unconfined \
  --tmpfs /run \
  --tmpfs /run/lock \
  -v /sys/fs/cgroup:/sys/fs/cgroup:ro \
  audio-linux/ci-systemd-trixie:local /lib/systemd/systemd --log-level=debug 2>&1 | sed -n '1,400p'
