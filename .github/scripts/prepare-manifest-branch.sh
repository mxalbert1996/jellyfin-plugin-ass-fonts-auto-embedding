#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: prepare-manifest-branch.sh <branch> <checkout-dir>" >&2
  exit 2
fi

: "${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

BRANCH="$1"
CHECKOUT_DIR="$2"
REMOTE_URL="https://x-access-token:${GITHUB_TOKEN}@github.com/${GITHUB_REPOSITORY}.git"

git clone "$REMOTE_URL" "$CHECKOUT_DIR"

git -C "$CHECKOUT_DIR" config user.name "github-actions[bot]"
git -C "$CHECKOUT_DIR" config user.email "41898282+github-actions[bot]@users.noreply.github.com"

if git -C "$CHECKOUT_DIR" ls-remote --exit-code --heads origin "$BRANCH" >/dev/null 2>&1; then
  git -C "$CHECKOUT_DIR" checkout -B "$BRANCH" "origin/$BRANCH"
else
  git -C "$CHECKOUT_DIR" checkout --orphan "$BRANCH"
  git -C "$CHECKOUT_DIR" rm -rf . >/dev/null 2>&1 || true
  printf '[]\n' > "$CHECKOUT_DIR/manifest.json"
  git -C "$CHECKOUT_DIR" add manifest.json
  git -C "$CHECKOUT_DIR" commit -m "Initialize $BRANCH"
  git -C "$CHECKOUT_DIR" push origin "$BRANCH"
fi
