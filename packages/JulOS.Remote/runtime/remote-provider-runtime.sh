#!/bin/sh
set -eu

umask 077

runtime_directory=/tmp/julos-remote-provider
key_file="$runtime_directory/json-secret-key"
token_file="$runtime_directory/nginx-token.conf"
nginx_config="$runtime_directory/nginx.conf"
guacd_pid_file="$runtime_directory/guacd.pid"
tomcat_pid_file="$runtime_directory/tomcat.pid"
nginx_pid_file="$runtime_directory/nginx.pid"
log_directory="$runtime_directory/logs"

report_failure() {
    code=$1
    detail=$2
    if [ -z "${JULOS_REMOTE_CALLBACK_ENDPOINT:-}" ] || [ -z "${JULOS_REMOTE_CALLBACK_TOKEN:-}" ] \
        || [ -z "${JULOS_REMOTE_SESSION_ID:-}" ] || [ -z "${JULOS_REMOTE_EXPECTED_REVISION:-}" ]; then
        return 0
    fi
    runtime_id="remote-$(printf '%s' "$JULOS_REMOTE_SESSION_ID" | tr -d '-')"
    body=$(printf '{"sessionId":"%s","runtimeId":"%s","event":"failed","expectedRevision":%s,"failureCode":"%s","failureDetail":"%s","retryable":true}' \
        "$JULOS_REMOTE_SESSION_ID" "$runtime_id" "$JULOS_REMOTE_EXPECTED_REVISION" "$code" "$detail")
    curl --silent --show-error --max-time 5 --request POST \
        --header "Content-Type: application/json" \
        --header "X-JulOS-Remote-Token: $JULOS_REMOTE_CALLBACK_TOKEN" \
        --data "$body" \
        "$JULOS_REMOTE_CALLBACK_ENDPOINT" >/dev/null 2>&1 || true
}

cleanup() {
    trap - EXIT INT TERM

    for pid_file in "$nginx_pid_file" "$tomcat_pid_file" "$guacd_pid_file"; do
        if [ -f "$pid_file" ]; then
            pid=$(cat "$pid_file")
            if kill -0 "$pid" 2>/dev/null; then
                kill "$pid" 2>/dev/null || true
            fi
        fi
    done

    for pid_file in "$nginx_pid_file" "$tomcat_pid_file" "$guacd_pid_file"; do
        if [ -f "$pid_file" ]; then
            pid=$(cat "$pid_file")
            wait "$pid" 2>/dev/null || true
        fi
    done

    rm -rf "$runtime_directory"
}

trap cleanup EXIT
trap 'exit 143' INT TERM

for name in JULOS_REMOTE_SESSION_ID JULOS_REMOTE_PROTOCOL JULOS_REMOTE_TARGET_HOST JULOS_REMOTE_TARGET_PORT \
    JULOS_REMOTE_MAXIMUM_SESSION_SECONDS JULOS_REMOTE_TARGET_CREDENTIAL JULOS_REMOTE_CALLBACK_ENDPOINT \
    JULOS_REMOTE_CALLBACK_TOKEN JULOS_REMOTE_EXPECTED_REVISION; do
    eval "value=\${$name:-}"
    if [ -z "$value" ]; then
        echo "$name is required." >&2
        exit 64
    fi
done

rm -rf "$runtime_directory"
install -d -m 0700 "$runtime_directory" "$log_directory" "$GUACAMOLE_HOME"

export GUACD_HOSTNAME=127.0.0.1
export GUACD_PORT=4822
export JSON_ENABLED=true

"/opt/julos-remote-provider/bridge/JulOS.Remote.ProviderBridge" generate-key "$key_file"
export JSON_SECRET_KEY
JSON_SECRET_KEY=$(cat "$key_file")

guacd -b 127.0.0.1 -l 4822 -f >"$log_directory/guacd.log" 2>&1 &
echo $! >"$guacd_pid_file"

attempt=0
while ! nc -z 127.0.0.1 4822; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 100 ]; then
        report_failure "remote.provider_guacd_unavailable" "guacd did not become ready."
        echo "guacd did not become ready." >&2
        exit 70
    fi
    sleep 0.2
done

/opt/guacamole/bin/entrypoint.sh >"$log_directory/tomcat.log" 2>&1 &
echo $! >"$tomcat_pid_file"

attempt=0
while ! nc -z 127.0.0.1 8080; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 150 ]; then
        report_failure "remote.provider_webapp_unavailable" "The Guacamole web application did not become ready."
        echo "The Guacamole web application did not become ready." >&2
        exit 70
    fi
    sleep 0.2
done

if ! "/opt/julos-remote-provider/bridge/JulOS.Remote.ProviderBridge" finalize "$key_file" "$token_file"; then
    echo "The provider bridge failed to finalize the Guacamole launch." >&2
    exit 70
fi

JULOS_PROVIDER_LISTEN_PORT=${JULOS_PROVIDER_LISTEN_PORT:-8081}
export JULOS_PROVIDER_LISTEN_PORT
envsubst '${JULOS_PROVIDER_LISTEN_PORT} ${JULOS_REMOTE_SESSION_ID}' \
    < /opt/julos-remote-provider/nginx.conf.template \
    > "$nginx_config"

nginx -c "$nginx_config" -g "daemon off;" >"$log_directory/nginx.log" 2>&1 &
echo $! >"$nginx_pid_file"

attempt=0
while ! nc -z 127.0.0.1 "$JULOS_PROVIDER_LISTEN_PORT"; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 100 ]; then
        report_failure "remote.provider_listener_unavailable" "The Remote display listener did not become ready."
        echo "The Remote display listener did not become ready." >&2
        exit 70
    fi
    sleep 0.2
done

if ! "/opt/julos-remote-provider/bridge/JulOS.Remote.ProviderBridge" connected; then
    echo "The provider bridge failed to report Remote display readiness." >&2
    exit 70
fi

set +e
wait "$(cat "$nginx_pid_file")"
exit_code=$?
set -e
exit "$exit_code"
