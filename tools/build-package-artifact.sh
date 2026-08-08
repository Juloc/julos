#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <package-directory> <output-zip>" >&2
  exit 64
fi

package_dir="$(realpath "$1")"
output_zip="$(realpath -m "$2")"
manifest="$package_dir/manifest.json"

if [[ ! -f "$manifest" ]]; then
  echo "package manifest not found: $manifest" >&2
  exit 66
fi

package_id="$(node -e 'const fs=require("fs"); const m=JSON.parse(fs.readFileSync(process.argv[1],"utf8").replace(/^﻿/,"")); process.stdout.write(m.PackageId)' "$manifest")"
package_version="$(node -e 'const fs=require("fs"); const m=JSON.parse(fs.readFileSync(process.argv[1],"utf8").replace(/^﻿/,"")); process.stdout.write(m.Version)' "$manifest")"
runtime_kind="$(node -e 'const fs=require("fs"); const m=JSON.parse(fs.readFileSync(process.argv[1],"utf8").replace(/^﻿/,"")); process.stdout.write(m.Runtime.Kind)' "$manifest")"

if [[ -z "$package_id" || -z "$package_version" ]]; then
  echo "package manifest is missing packageId or version" >&2
  exit 65
fi

stage="$(mktemp -d)"
cleanup() {
  rm -rf "$stage"
}
trap cleanup EXIT

cp "$manifest" "$stage/manifest.json"

if [[ -d "$package_dir/frontend" ]]; then
  cp -a "$package_dir/frontend" "$stage/frontend"
fi

if [[ -f "$package_dir/settings.schema.json" ]]; then
  cp "$package_dir/settings.schema.json" "$stage/settings.schema.json"
fi

if [[ -d "$package_dir/migrations" ]]; then
  cp -a "$package_dir/migrations" "$stage/migrations"
fi

case "$runtime_kind" in
  process)
    worker_project="$(find "$package_dir/worker" -maxdepth 1 -type f -name '*.csproj' -print -quit)"
    if [[ -z "$worker_project" ]]; then
      echo "process package has no worker project: $package_id" >&2
      exit 65
    fi
    mkdir -p "$stage/worker"
    dotnet publish "$worker_project" \
      --configuration Release \
      --output "$stage/worker" \
      --no-self-contained \
      -p:DebugSymbols=false \
      -p:DebugType=None
    ;;
  none)
    ;;
  *)
    echo "unsupported official package runtime kind: $runtime_kind" >&2
    exit 65
    ;;
esac

mkdir -p "$(dirname "$output_zip")"
rm -f "$output_zip"

# Stable timestamps and lexical file order make the archive reproducible across runs.
find "$stage" -type f -exec touch -d '2000-01-01 00:00:00 UTC' {} +
(
  cd "$stage"
  find . -type f -print | LC_ALL=C sort | zip -X -q "$output_zip" -@
)

archive_digest="$(sha256sum "$output_zip" | cut -d' ' -f1)"
archive_name="$(basename "$output_zip")"
printf '%s  %s\n' "$archive_digest" "$archive_name" > "$output_zip.sha256"

printf 'built %s %s\n' "$package_id" "$package_version"
printf 'archive: %s\n' "$output_zip"
printf 'sha256: %s\n' "$archive_digest"
