#!/bin/sh
set -eu

umask 077

backup_root=${JULOS_BACKUP_ROOT:-./artifacts/backups}
package_root=${JULOS_PACKAGE_ROOT:-./packages-data}
connection=${JULOS_BACKUP_POSTGRES:-${ConnectionStrings__CoreDatabase:-}}

if [ -z "$connection" ]; then
  echo "Set JULOS_BACKUP_POSTGRES or ConnectionStrings__CoreDatabase." >&2
  exit 2
fi

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
final="$backup_root/$timestamp"
temporary="$backup_root/.${timestamp}.partial"
mkdir -p "$backup_root"
rm -rf "$temporary"
mkdir -p "$temporary"

cleanup() {
  rm -rf "$temporary"
}
trap cleanup EXIT HUP INT TERM

pg_dump --dbname "$connection" --format=custom --no-owner --no-privileges --file "$temporary/core.pgdump"

if [ -d "$package_root" ]; then
  tar --create --gzip --file "$temporary/package-data.tar.gz" --directory "$package_root" .
else
  tar --create --gzip --file "$temporary/package-data.tar.gz" --files-from /dev/null
fi

version=$(tr -d '\r\n' < VERSION)
cat > "$temporary/metadata.json" <<EOF
{
  "schemaVersion": 1,
  "createdAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "julosVersion": "$version",
  "databaseFormat": "postgresql-custom",
  "packageDataIncluded": true
}
EOF

(
  cd "$temporary"
  sha256sum core.pgdump package-data.tar.gz metadata.json > SHA256SUMS
)

mv "$temporary" "$final"
trap - EXIT HUP INT TERM
printf '%s\n' "$final"
