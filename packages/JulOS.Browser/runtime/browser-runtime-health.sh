#!/bin/sh
set -eu

# Keep this default aligned with browser-runtime.sh.
runtime_directory="${XDG_RUNTIME_DIR:-/run/julos-browser}"

for process_name in xvfb openbox x11vnc chromium; do
    pid_file="$runtime_directory/$process_name.pid"
    if [ ! -s "$pid_file" ]; then
        echo "Missing runtime PID file: $pid_file" >&2
        exit 1
    fi

    pid="$(cat "$pid_file")"
    case "$pid" in
        ''|*[!0-9]*)
            echo "Invalid runtime PID for $process_name: $pid" >&2
            exit 1
            ;;
    esac

    if ! kill -0 "$pid" 2>/dev/null; then
        echo "Runtime process is not running: $process_name (PID $pid)" >&2
        exit 1
    fi
done

if ! nc -z 127.0.0.1 5900; then
    echo "VNC listener is unavailable on 127.0.0.1:5900." >&2
    exit 1
fi
