#!/usr/bin/env bash
set -euo pipefail

[ $# -eq 1 ] || { echo "usage: tag-release.sh vX.Y.Z" >&2; exit 2; }
tag="$1"
[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "tag must look like v1.2.3" >&2; exit 2; }
repos=(${RELEASE_REPOS:-3h4524/CAPSTONE_BE 3h4524/CAPSTONE_FE})

for repo in "${repos[@]}"; do
  if gh api "repos/$repo/git/ref/tags/$tag" > /dev/null 2>&1; then
    echo "$repo already has $tag" >&2
    exit 1
  fi
done

for repo in "${repos[@]}"; do
  sha="$(gh api "repos/$repo/git/ref/heads/production" -q .object.sha)"
  gh api "repos/$repo/git/refs" -f ref="refs/tags/$tag" -f sha="$sha" > /dev/null
  echo "$repo: $tag -> ${sha:0:7} (production)"
done

echo "Both deploys started: gh run list -R <repo> -w 'Deploy production' -L 1"
