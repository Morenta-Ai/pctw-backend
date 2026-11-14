#!/usr/bin/env bash
# Copies the clients' API contracts into docs/contracts, from sibling clones of the client repos.
# Run it after a client changes its contract, then fix whatever the contract tests report.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
parent="$(dirname "$root")"
for pair in user:pctw-user collector:pctw-collector admin:pctw-admin; do
  name="${pair%%:*}"
  repo="${pair#*:}"
  source="$parent/$repo/docs/API_CONTRACT.md"
  if [[ -f "$source" ]]; then
    cp "$source" "$root/docs/contracts/$name.md"
    echo "Copied $repo → docs/contracts/$name.md"
  else
    echo "Skipped $repo: $source not found" >&2
  fi
done
