#!/usr/bin/env bash
set -euo pipefail

# Ensure XDG_RUNTIME_DIR is set to a valid directory
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/tmp/runtime-dir}"
mkdir -p "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"

# Validate PDF arguments: two inputs and an optional output filename
if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "Usage: diff-pdf <source1>.pdf <source2>.pdf [output.pdf]"
  exit 1
fi

SOURCE1="$1"
SOURCE2="$2"
# Default output filename is diff.pdf if not provided
OUTPUT="${3:-diff.pdf}"

# Launch a virtual X server for GTK, suppressing keyboard warnings
Xvfb :99 -screen 0 1024x768x24 2>/dev/null &
export DISPLAY=:99
# Allow Xvfb to start
sleep 1

# Run diff-pdf with output flag, creating the specified diff file in /data
exec /usr/local/bin/diff-pdf "$SOURCE1" "$SOURCE2" --output-diff="$OUTPUT"