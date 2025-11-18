#!/usr/bin/env bash
set -euo pipefail

echo "This diagnostic script has been deprecated."

docker rm -f debug-cg-host 2>/dev/null || true

# Run the container detached; allow it to fail silently so we can inspect
docker run --name debug-cg-host -d \
  --privileged \
  --cgroupns=host \
  --security-opt seccomp=unconfined \
  --tmpfs /run \
  --tmpfs /run/lock \
  -v /sys/fs/cgroup:/sys/fs/cgroup:ro \
  audio-linux/ci-systemd-trixie:local /lib/systemd/systemd || true

sleep 3

echo "==== docker ps -a (filtered) ===="
docker ps -a --filter "name=debug-cg-host" --format "table {{.ID}}\t{{.Image}}\t{{.Status}}\t{{.Names}}"

echo "==== docker inspect ===="
docker inspect debug-cg-host || true

echo "==== docker logs (tail 500) ===="
docker logs --tail 500 debug-cg-host || true

# Try to show the raw json log file from Docker's container root (may require sudo)
cid=$(docker inspect --format='{{.Id}}' debug-cg-host 2>/dev/null || true)
if [ -n "$cid" ]; then
  jsonpath="/var/lib/docker/containers/$cid/$cid-json.log"
  if [ -f "$jsonpath" ]; then
    echo "==== raw container json log (tail 500) ===="
    sudo sh -c "echo '--- json log start ---'; tail -n 500 '$jsonpath'; echo '--- json log end ---'"
  else
    echo "json log file not found at $jsonpath - daemon may use a different root (e.g., /var/lib/docker/overlay2/...); try 'docker info' to locate Docker Root Dir"
    docker info --format '{{json .}}' || true
  fi
else
  echo 'no container id available'
fi

# If container is still running, open an interactive shell (user can exec manually later)
status=$(docker inspect --format='{{.State.Status}}' debug-cg-host 2>/dev/null || true)
if [ "$status" = "running" ]; then
  echo "Container debug-cg-host is running; you can exec into it with: docker exec -it debug-cg-host /bin/bash"
fi

exit 0

### Additional inspection: locate container folder under Docker Root Dir and list files
# Sometimes docker inspect with the name can be unreliable because of quoting in callers;
# determine container id via docker ps output and then show files from /var/lib/docker/containers/<id>
echo "\n=== Additional host-level container folder inspection ==="
cid=$(docker ps -a --filter name=debug-cg-host --format '{{.ID}}' | tr -d '\n' || true)
if [ -n "$cid" ]; then
  echo "resolved cid=$cid"
  echo "docker inspect LogPath:"
  docker inspect --format '{{.LogPath}}' $cid || true
  echo "ls -la /var/lib/docker/containers/$cid (requires sudo):"
  sudo ls -la /var/lib/docker/containers/$cid || true
  echo "tail any files under /var/lib/docker/containers/$cid (requires sudo):"
  sudo sh -c 'for f in /var/lib/docker/containers/'"$cid"'/*; do echo "--- $f ---"; tail -n 500 "$f" || true; done'
else
  echo "could not resolve container id for debug-cg-host"
fi
