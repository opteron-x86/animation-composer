#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$project_dir/dist/linux-x64/AnimationComposer" --source /mnt/mods/Amaranth
