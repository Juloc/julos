#!/usr/bin/env pwsh
# Windows entry point for repository validation.
# The checks themselves live in tools/validate.mjs so both platforms run the same logic.

$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'validate.mjs'

& node $script @args

exit $LASTEXITCODE
