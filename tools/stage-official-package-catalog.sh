#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <output-directory> <digest-pinned-browser-runtime-image>" >&2
  exit 64
fi

output_dir="$(realpath -m "$1")"
browser_runtime_image="$2"
adaptive_browser_pin_file="deploy/runtime-pins/adaptive-browser-runtime.txt"
adaptive_browser_runtime_image="${JULOS_ADAPTIVE_BROWSER_RUNTIME_IMAGE:-}"
if [[ -z "$adaptive_browser_runtime_image" && -f "$adaptive_browser_pin_file" ]]; then
  adaptive_browser_runtime_image="$(tr -d '\r\n' < "$adaptive_browser_pin_file")"
fi
if [[ ! "$adaptive_browser_runtime_image" =~ ^ghcr\.io/[a-z0-9./_-]+@sha256:[0-9a-f]{64}$ ]]; then
  echo "Adaptive Browser runtime must be published and digest-pinned in $adaptive_browser_pin_file before staging official packages." >&2
  exit 78
fi

if [[ -z "${PACKAGE_SIGNING_KEY:-}" || -z "${PACKAGE_KEY_ID:-}" ]]; then
  echo "PACKAGE_SIGNING_KEY and PACKAGE_KEY_ID are required for official package staging." >&2
  exit 78
fi

package_version() {
  node -e '
    const fs = require("fs");
    const manifest = JSON.parse(fs.readFileSync(process.argv[1], "utf8").replace(/^﻿/, ""));
    if (typeof manifest.Version !== "string" || manifest.Version.length === 0) process.exit(65);
    process.stdout.write(manifest.Version);
  ' "$1"
}

browser_version="$(package_version packages/JulOS.Browser/manifest.json)"
adaptive_browser_version="$(package_version packages/JulOS.AdaptiveBrowser/manifest.json)"
remote_version="$(package_version packages/JulOS.Remote/manifest.json)"
hostmetrics_version="$(package_version packages/JulOS.HostMetrics/manifest.json)"

mkdir -p "$output_dir"
find "$output_dir" -mindepth 1 -maxdepth 1 ! -name '.gitkeep' -delete

# Process workers are published for Linux. Restore the matching runtime graph
# explicitly so package creation is deterministic in clean CI containers.
dotnet restore packages/JulOS.Remote/worker/JulOS.Remote.Worker.csproj --locked-mode --runtime linux-x64
dotnet restore packages/JulOS.HostMetrics/worker/JulOS.HostMetrics.Worker.csproj --locked-mode --runtime linux-x64

bash tools/build-package-artifact.sh packages/JulOS.Browser "$output_dir/JulOS.Browser-$browser_version.zip"
bash tools/build-package-artifact.sh packages/JulOS.AdaptiveBrowser "$output_dir/JulOS.AdaptiveBrowser-$adaptive_browser_version.zip"
bash tools/build-package-artifact.sh packages/JulOS.Remote "$output_dir/JulOS.Remote-$remote_version.zip"
bash tools/build-package-artifact.sh packages/JulOS.HostMetrics "$output_dir/JulOS.HostMetrics-$hostmetrics_version.zip"

PACKAGE_PUBLISHER_ID="${PACKAGE_PUBLISHER_ID:-juloc-official}" \
  node tools/build-official-package-catalog.mjs \
    "$output_dir" \
    "$browser_runtime_image" \
    "$adaptive_browser_runtime_image"

# Per-package .sha256 files are build intermediates. catalog.json is the signed
# store index consumed by JulOS and already carries every artifact digest.
rm -f "$output_dir"/*.zip.sha256
