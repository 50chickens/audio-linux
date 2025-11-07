#!/bin/bash
set -e

DIR="${1:-/tmp}"

# Output available space in MB for the given directory
df -m "$DIR" | awk 'NR==2 {print $4}'