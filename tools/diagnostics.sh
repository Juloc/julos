#!/bin/sh
set -eu

umask 077

output_root=${JULOS_DIAGNOSTIC_ROOT:-./artifacts/diagnostics}
server_url=${JULOS_SERVER_URL:-http://127.0.0.1:8080}
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
bundle="$output_root/julos-diagnostics-$timestamp"
mkdir -p "$bundle"

version=$(tr -d '\r\n' < VERSION)
cat > "$bundle/summary.txt" <<EOF
JulOS version: $version
Observed at UTC: $(date -u +%Y-%m-%dT%H:%M:%SZ)
Kernel: $(uname -sr)
Architecture: $(uname -m)
EOF

if command -v curl >/dev/null 2>&1; then
  curl --silent --show-error --max-time 10 "$server_url/health/live" > "$bundle/health-live.json" 2> "$bundle/health-live-error.txt" || true
  curl --silent --show-error --max-time 10 "$server_url/health/ready" > "$bundle/health-ready.json" 2> "$bundle/health-ready-error.txt" || true
fi

if command -v docker >/dev/null 2>&1; then
  docker ps \
    --filter label=com.juloc.julos.managed=true \
    --format '{{.Names}}\t{{.Image}}\t{{.Status}}' \
    > "$bundle/managed-containers.txt" 2> "$bundle/docker-error.txt" || true
fi

find "$bundle" -type f -size 0 -delete
(
  cd "$output_root"
  tar --create --gzip --file "julos-diagnostics-$timestamp.tar.gz" "julos-diagnostics-$timestamp"
)
rm -rf "$bundle"
printf '%s\n' "$output_root/julos-diagnostics-$timestamp.tar.gz"
