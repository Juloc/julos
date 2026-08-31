#!/bin/sh
set -eu

umask 077
runtime_directory=/tmp/julos-adaptive-browser
profile_directory="$runtime_directory/profile"
log_directory="$runtime_directory/logs"
chromium_pid_file="$runtime_directory/chromium.pid"
bridge_pid_file="$runtime_directory/bridge.pid"

cleanup() {
    trap - EXIT INT TERM
    for pid_file in "$bridge_pid_file" "$chromium_pid_file"; do
        if [ -f "$pid_file" ]; then
            pid=$(cat "$pid_file")
            if kill -0 "$pid" 2>/dev/null; then
                kill "$pid" 2>/dev/null || true
            fi
        fi
    done
    for pid_file in "$bridge_pid_file" "$chromium_pid_file"; do
        if [ -f "$pid_file" ]; then
            wait "$(cat "$pid_file")" 2>/dev/null || true
        fi
    done
    rm -rf "$runtime_directory"
}

trap cleanup EXIT
trap 'exit 143' INT TERM

if [ -z "${JULOS_BROWSER_STREAM_TOKEN:-}" ]; then
    echo "JULOS_BROWSER_STREAM_TOKEN is required." >&2
    exit 64
fi

start_url=${JULOS_START_URL:-about:blank}
case "$start_url" in
    about:blank|http://*|https://*) ;;
    *) echo "JULOS_START_URL must use http, https or about:blank." >&2; exit 64 ;;
esac

width=${JULOS_BROWSER_VIEWPORT_WIDTH:-1280}
height=${JULOS_BROWSER_VIEWPORT_HEIGHT:-800}
scale=${JULOS_BROWSER_DEVICE_SCALE_FACTOR:-1}
case "$width" in *[!0-9]*|'') echo "JULOS_BROWSER_VIEWPORT_WIDTH is invalid." >&2; exit 64 ;; esac
case "$height" in *[!0-9]*|'') echo "JULOS_BROWSER_VIEWPORT_HEIGHT is invalid." >&2; exit 64 ;; esac

rm -rf "$runtime_directory"
install -d -m 0700 "$profile_directory" "$log_directory"

chromium \
    --headless=new \
    --no-sandbox \
    --disable-dev-shm-usage \
    --disable-background-networking \
    --disable-component-update \
    --disable-default-apps \
    --disable-features=MediaRouter,Translate \
    --disable-sync \
    --enable-webgl \
    --enable-unsafe-swiftshader \
    --ignore-gpu-blocklist \
    --use-gl=angle \
    --use-angle=swiftshader-webgl \
    --metrics-recording-only \
    --no-default-browser-check \
    --no-first-run \
    --password-store=basic \
    --remote-debugging-address=127.0.0.1 \
    --remote-debugging-port=9222 \
    --user-data-dir="$profile_directory" \
    --window-size="${width},${height}" \
    --force-device-scale-factor="$scale" \
    "$start_url" \
    >"$log_directory/chromium.log" 2>&1 &
echo $! >"$chromium_pid_file"

attempt=0
while ! nc -z 127.0.0.1 9222; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 300 ]; then
        echo "Chromium DevTools endpoint did not become ready." >&2
        exit 70
    fi
    if ! kill -0 "$(cat "$chromium_pid_file")" 2>/dev/null; then
        echo "Chromium exited during startup." >&2
        exit 70
    fi
    sleep 0.1
done

/opt/julos-adaptive-browser/JulOS.AdaptiveBrowser.Runtime \
    >"$log_directory/bridge.log" 2>&1 &
echo $! >"$bridge_pid_file"

attempt=0
while ! nc -z 127.0.0.1 8080; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 200 ]; then
        echo "Adaptive Browser stream endpoint did not become ready." >&2
        exit 70
    fi
    if ! kill -0 "$(cat "$bridge_pid_file")" 2>/dev/null; then
        echo "Adaptive Browser stream bridge exited during startup." >&2
        exit 70
    fi
    sleep 0.1
done

set +e
wait "$(cat "$bridge_pid_file")"
exit_code=$?
set -e
exit "$exit_code"
