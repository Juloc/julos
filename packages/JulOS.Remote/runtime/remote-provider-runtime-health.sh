#!/bin/sh
set -eu

runtime_directory=/tmp/julos-remote-provider

for process_name in guacd tomcat nginx; do
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

listen_port=${JULOS_PROVIDER_LISTEN_PORT:-8081}
if ! nc -z 127.0.0.1 "$listen_port"; then
    echo "The Remote display listener is unavailable on 127.0.0.1:$listen_port." >&2
    exit 1
fi
