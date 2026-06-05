#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo "Usage: $(basename "$0") <actual-pdf> [expected-pdf]" >&2
  exit 2
fi

actual_pdf="$1"
expected_pdf="${2:-test/output-diff-expected.pdf}"

test -s "$actual_pdf"
test -s "$expected_pdf"

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

print_hashes() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$expected_pdf" "$actual_pdf" >&2
  else
    shasum -a 256 "$expected_pdf" "$actual_pdf" >&2
  fi
}

if command -v pdftoppm >/dev/null 2>&1; then
  pdftoppm -r 72 -png -singlefile "$expected_pdf" "$tmpdir/expected" >/dev/null
  pdftoppm -r 72 -png -singlefile "$actual_pdf" "$tmpdir/actual" >/dev/null

  if ! cmp -s "$tmpdir/expected.png" "$tmpdir/actual.png"; then
    echo "Rendered generated PDF does not match $expected_pdf." >&2
    print_hashes
    exit 1
  fi

  exit 0
fi

normalize_pdf() {
  perl -0pe 's#/CreationDate \(D:[^)]*\)#/CreationDate (D:00000000000000Z)#g' "$1"
}

normalize_pdf "$expected_pdf" > "$tmpdir/expected.pdf"
normalize_pdf "$actual_pdf" > "$tmpdir/actual.pdf"

if ! cmp -s "$tmpdir/expected.pdf" "$tmpdir/actual.pdf"; then
  echo "Generated PDF does not match $expected_pdf after normalizing CreationDate." >&2
  print_hashes
  exit 1
fi
