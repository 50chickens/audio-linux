#!/bin/bash
set -e

tmp_free=$(df -m /tmp | awk 'NR==2 {print $4}')
home_free=$(df -m ~ | awk 'NR==2 {print $4}')

echo "/tmp free space: ${tmp_free} MB"
echo "Home directory free space: ${home_free} MB"

if [[ $tmp_free -lt 100 && $home_free -lt 100 ]]; then
  echo "Warning: Both /tmp and home have less than 100MB free."
  exit 1
fi

echo "Disk space requirements met."
exit 0