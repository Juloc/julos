#!/bin/sh
set -eu

umask 077

if [ "$#" -ne 2 ] || [ "$2" != "--confirm-destructive-restore" ]; then
  echo "Usage: tools/restore.sh <backup-directory> --confirm-destructive-restore" >&2
  exit 2
fi

backup=$1
package_root=${JULOS_PACKAGE_ROOT:-./packages-data}
connection=${JULOS_RESTORE_POSTGRES:-${ConnectionStrings__CoreDatabase:-}}
julos_server=${JULOS_SERVER_COMMAND:-dotnet /application/JulOS.Server.dll}

if [ -z "$connection" ]; then
  echo "Set JULOS_RESTORE_POSTGRES or ConnectionStrings__CoreDatabase." >&2
  exit 2
fi

for required in SHA256SUMS package-data.tar.gz metadata.json; do
  if [ ! -f "$backup/$required" ]; then
    echo "Backup is missing $required." >&2
    exit 3
  fi
done

# The archive states its own database format, so a PostgreSQL dump can never be applied
# to a SQLite deployment or the other way round.
database_format=$(sed -n 's/.*"databaseFormat"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$backup/metadata.json")
case "$database_format" in
  sqlite) database_file=core.db ;;
  postgresql-custom) database_file=core.pgdump ;;
  *)
    echo "Backup declares unsupported database format '$database_format'." >&2
    exit 3
    ;;
esac

if [ ! -f "$backup/$database_file" ]; then
  echo "Backup is missing $database_file." >&2
  exit 3
fi

(
  cd "$backup"
  sha256sum --check SHA256SUMS
)

metadata_version=$(sed -n 's/.*"julosVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$backup/metadata.json")
if [ -z "$metadata_version" ]; then
  echo "Backup metadata has no JulOS version." >&2
  exit 3
fi

case "$database_format" in
  sqlite)
    # Server verifies the archive with integrity_check before it replaces anything, so a
    # corrupt backup cannot destroy a working database (decision D043).
    # shellcheck disable=SC2086
    $julos_server --restore-database "$backup/$database_file"
    ;;
  postgresql-custom)
    pg_restore \
      --dbname "$connection" \
      --clean \
      --if-exists \
      --no-owner \
      --no-privileges \
      --single-transaction \
      "$backup/$database_file"
    ;;
esac

parent=$(dirname "$package_root")
staging="$parent/.julos-package-restore-$$"
previous="$parent/.julos-package-previous-$$"
rm -rf "$staging" "$previous"
mkdir -p "$staging"
tar --extract --gzip --file "$backup/package-data.tar.gz" --directory "$staging"
if [ -e "$package_root" ]; then
  mv "$package_root" "$previous"
fi
mv "$staging" "$package_root"
rm -rf "$previous"

printf 'Restored JulOS backup created by version %s.\n' "$metadata_version"
