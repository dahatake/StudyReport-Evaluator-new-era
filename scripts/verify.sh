#!/usr/bin/env bash
# Runs the same gates as scripts/verify.ps1 (requires PowerShell 7 and the .NET SDK).
set -euo pipefail
exec pwsh -NoProfile -File "$(dirname "$0")/verify.ps1" "$@"
