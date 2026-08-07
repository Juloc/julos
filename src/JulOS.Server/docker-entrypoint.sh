#!/bin/sh
set -eu

runtime_uid="${APP_UID:?APP_UID is required}"
runtime_gid="$runtime_uid"

# Named volumes can predate the current image and therefore retain root-owned
# directories. Repair only JulOS-owned writable paths, then immediately drop
# privileges before the application starts.
for directory in \
    /var/lib/julos/data \
    /var/lib/julos/packages \
    /var/lib/julos/data-protection
do
    mkdir -p "$directory"
    chown -R "$runtime_uid:$runtime_gid" "$directory"
done

exec gosu "$runtime_uid:$runtime_gid" "$@"
