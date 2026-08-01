#!/usr/bin/env sh
# Unix entry point for repository validation.
# The checks themselves live in tools/validate.mjs so both platforms run the same logic.

set -eu

script_directory=$(cd "$(dirname "$0")" && pwd)

exec node "$script_directory/validate.mjs" "$@"
