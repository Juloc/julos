#!/bin/sh
set -eu

runtime_directory=/tmp/julos-browser

for process_name in xvfb openbox x11vnc chromium; do
    pid_file="$runtime_directory/$process_name.pid"
    test -s "$pid_file"
    pid=$(cat "$pid_file")
    kill -0 "$pid" 2>/dev/null
 done

nc -z 127.0.0.1 5900
