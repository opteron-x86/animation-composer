#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
dotnet_bin="${MOVESETFORGE_DOTNET:-dotnet}"
version="${2:-$(python3 tools/release_version.py)}"
if [[ ! "$version" =~ ^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){2}$ ]]; then
    echo "Expected a major.minor.patch version, got '$version'" >&2
    exit 1
fi
for rid in linux-x64 win-x64; do
    "$dotnet_bin" publish src/MovesetForge.Desktop -c Release -r "$rid" --self-contained true -o "dist/$rid" -m:1 /p:UseSharedCompilation=false -p:Version="$version"
done
python3 tools/package.py "${1:-dist/packages}" --version "$version"
