#!/usr/bin/env bash
set -euo pipefail

# Ensure XDG_RUNTIME_DIR is set to a valid directory
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/tmp/runtime-dir}"
mkdir -p "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"

VERIFY=false
ARGS=()

for arg in "$@"; do
  case "$arg" in
    -verify|--verify)
      VERIFY=true
      ;;
    *)
      ARGS+=("$arg")
      ;;
  esac
done

set -- "${ARGS[@]}"

# Validate PDF arguments: two inputs, an optional output filename, and optional -verify
if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "Usage: diff-pdf-b [-verify] <source1>.pdf <source2>.pdf [output.pdf]"
  exit 1
fi

SOURCE1="$1"
SOURCE2="$2"
# Default output filename is diff.pdf if not provided
OUTPUT="${3:-diff.pdf}"
EXPECTED_OUTPUT="output-diff-expected.pdf"

# Launch a virtual X server for GTK, suppressing keyboard warnings
Xvfb :99 -screen 0 1024x768x24 2>/dev/null &
XVFB_PID="$!"
trap 'kill "$XVFB_PID" >/dev/null 2>&1 || true' EXIT
export DISPLAY=:99
# Allow Xvfb to start
sleep 1

# Run diff-pdf-b with output flag, creating the specified diff file in /data.
# diff-pdf-b can return nonzero when the source PDFs differ, which is expected here.
set +e
/usr/local/bin/diff-pdf-b "$SOURCE1" "$SOURCE2" --output-diff="$OUTPUT"
DIFF_STATUS="$?"
set -e

if [ "$VERIFY" = true ]; then
  /usr/local/bin/assert-output-pdf-matches-expected.sh "$OUTPUT" "$EXPECTED_OUTPUT"
  exit 0
fi

exit "$DIFF_STATUS"
