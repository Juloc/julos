#!/bin/sh
set -eu

umask 077

backup_root=${JULOS_BACKUP_ROOT:-./artifacts/backups}
package_root=${JULOS_PACKAGE_ROOT:-./packages-data}
connection=${JULOS_BACKUP_POSTGRES:-${ConnectionStrings__CoreDatabase:-}}
provider=${JULOS_BACKUP_PROVIDER:-${Database__Provider:-}}

if [ -z "$connection" ]; then
  echo "Set JULOS_BACKUP_POSTGRES or ConnectionStrings__CoreDatabase." >&2
  exit 2
fi

# The default core store is SQLite (decision D033). When the provider is not stated
# explicitly, a SQLite connection string is recognised by its Data Source keyword; a
# PostgreSQL connection string never contains one.
if [ -z "$provider" ]; then
  case "$connection" in
    *[Dd]ata\ [Ss]ource=*) provider=sqlite ;;
    *) provider=postgresql ;;
  esac
fi

# The SQLite copy is taken by the Server itself through SQLite's online backup API
# (decision D043), so the runtime image needs no sqlite3 command-line tool.
julos_server=${JULOS_SERVER_COMMAND:-dotnet /application/JulOS.Server.dll}

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

case "$provider" in
  sqlite)
    database_file=core.db
    database_format=sqlite
    # shellcheck disable=SC2086
    $julos_server --backup-database "$temporary/$database_file" >/dev/null
    ;;
  postgresql)
    database_file=core.pgdump
    database_format=postgresql-custom
    pg_dump --dbname "$connection" --format=custom --no-owner --no-privileges --file "$temporary/$database_file"
    ;;
  *)
    echo "Unsupported core database provider '$provider'." >&2
    exit 2
    ;;
esac

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
  "databaseFormat": "$database_format",
  "packageDataIncluded": true
}
EOF

(
  cd "$temporary"
  sha256sum "$database_file" package-data.tar.gz metadata.json > SHA256SUMS
)

mv "$temporary" "$final"
trap - EXIT HUP INT TERM
printf '%s\n' "$final"
