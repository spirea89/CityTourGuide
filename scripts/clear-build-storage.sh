#!/usr/bin/env bash
set -euo pipefail

SOL_ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
INCLUDE_NUGET="${INCLUDE_NUGET_CACHE:-0}"
INCLUDE_WORKLOADS="${INCLUDE_WORKLOADS:-0}"

if [ ! -d "$SOL_ROOT" ]; then
  echo "Solution root '$SOL_ROOT' was not found." >&2
  exit 1
fi

echo "Cleaning build artifacts under $SOL_ROOT"

reclaimed=0
while IFS= read -r -d '' dir; do
  size=$(du -sb "$dir" 2>/dev/null | cut -f1)
  rm -rf "$dir"
  echo "Deleted $dir"
  if [[ -n "$size" ]]; then
    reclaimed=$((reclaimed + size))
  fi
done < <(find "$SOL_ROOT" -type d \( -name bin -o -name obj \) -print0)

if [[ "$INCLUDE_NUGET" == "1" ]]; then
  echo "Clearing NuGet cache..."
  dotnet nuget locals all --clear || echo "Failed to clear NuGet cache" >&2
fi

if [[ "$INCLUDE_WORKLOADS" == "1" ]]; then
  echo "Cleaning unused workloads..."
  dotnet workload clean || echo "Failed to clean workloads" >&2
fi

echo "Approximately $(awk "BEGIN {printf \"%.2f\", $reclaimed/1024/1024}") MB reclaimed from project build folders."
