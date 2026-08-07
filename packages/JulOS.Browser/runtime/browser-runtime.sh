#!/bin/sh
set -eu

umask 077

runtime_directory=/tmp/julos-browser
password_file="$runtime_directory/vnc-password"
x_pid_file="$runtime_directory/xvfb.pid"
window_manager_pid_file="$runtime_directory/openbox.pid"
vnc_pid_file="$runtime_directory/x11vnc.pid"
chromium_pid_file="$runtime_directory/chromium.pid"
profile_directory="$runtime_directory/profile"
log_directory="$runtime_directory/logs"

cleanup() {
    trap - EXIT INT TERM

    for pid_file in "$chromium_pid_file" "$vnc_pid_file" "$window_manager_pid_file" "$x_pid_file"; do
        if [ -f "$pid_file" ]; then
            pid=$(cat "$pid_file")
            if kill -0 "$pid" 2>/dev/null; then
                kill "$pid" 2>/dev/null || true
            fi
        fi
    done

    for pid_file in "$chromium_pid_file" "$vnc_pid_file" "$window_manager_pid_file" "$x_pid_file"; do
        if [ -f "$pid_file" ]; then
            pid=$(cat "$pid_file")
            wait "$pid" 2>/dev/null || true
        fi
    done

    rm -rf "$runtime_directory"
}

trap cleanup EXIT
trap 'exit 143' INT TERM

if [ -z "${JULOS_VNC_PASSWORD:-}" ]; then
    echo "JULOS_VNC_PASSWORD is required." >&2
    exit 64
fi

if ! printf '%s' "$JULOS_VNC_PASSWORD" | LC_ALL=C grep -Eq '^[!-~]{8}$'; then
    echo "JULOS_VNC_PASSWORD must contain exactly eight printable ASCII characters." >&2
    exit 64
fi

start_url=${JULOS_START_URL:-about:blank}
case "$start_url" in
    about:blank|http://*|https://*) ;;
    *)
        echo "JULOS_START_URL must use http, https or about:blank." >&2
        exit 64
        ;;
esac

rm -rf "$runtime_directory"
install -d -m 0700 "$runtime_directory" "$profile_directory" "$log_directory"

x11vnc -storepasswd "$JULOS_VNC_PASSWORD" "$password_file" >/dev/null 2>&1
unset JULOS_VNC_PASSWORD

Xvfb "$DISPLAY" \
    -screen 0 "$JULOS_SCREEN_GEOMETRY" \
    -dpi "$JULOS_SCREEN_DPI" \
    -nolisten tcp \
    -noreset \
    >"$log_directory/xvfb.log" 2>&1 &
echo $! >"$x_pid_file"

attempt=0
while [ ! -S "/tmp/.X11-unix/X${DISPLAY#:}" ]; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 100 ]; then
        echo "Xvfb did not become ready." >&2
        exit 70
    fi
    sleep 0.1
done

openbox >"$log_directory/openbox.log" 2>&1 &
echo $! >"$window_manager_pid_file"

x11vnc \
    -display "$DISPLAY" \
    -rfbauth "$password_file" \
    -rfbport 5900 \
    -listen 0.0.0.0 \
    -no6 \
    -forever \
    -shared \
    -repeat \
    -noxdamage \
    -nosel \
    -noprimary \
    >"$log_directory/x11vnc.log" 2>&1 &
echo $! >"$vnc_pid_file"

attempt=0
while ! nc -z 127.0.0.1 5900; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 100 ]; then
        echo "The VNC display endpoint did not become ready." >&2
        exit 70
    fi
    sleep 0.1
done

chromium \
    --no-sandbox \
    --disable-dev-shm-usage \
    --disable-gpu \
    --disable-background-networking \
    --disable-component-update \
    --disable-default-apps \
    --disable-features=MediaRouter,Translate \
    --disable-sync \
    --metrics-recording-only \
    --no-default-browser-check \
    --no-first-run \
    --password-store=basic \
    --ozone-platform=x11 \
    --user-data-dir="$profile_directory" \
    --window-position=0,0 \
    --window-size=1280,800 \
    "$start_url" \
    >"$log_directory/chromium.log" 2>&1 &
echo $! >"$chromium_pid_file"

set +e
wait "$(cat "$chromium_pid_file")"
exit_code=$?
set -e
exit "$exit_code"
