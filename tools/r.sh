#!/usr/bin/env bash
# WSL wrapper (Ref §2.6): runs the Windows dotnet.exe or pwsh.exe with the working directory C:\dev\uas-sort.
# usage: tools/r.sh dotnet <args...>   |   tools/r.sh pwsh <args...>
# Environment variables reach Windows only through WSLENV; add your own (e.g. UASSORT_GOLDEN) the same way.
set -uo pipefail
cd /mnt/c/dev/uas-sort || exit 1
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false DOTNET_CLI_TELEMETRY_OPTOUT=1
export WSLENV="${WSLENV:+$WSLENV:}MSBUILDDISABLENODEREUSE:DOTNET_CLI_USE_MSBUILD_SERVER:UseSharedCompilation:DOTNET_CLI_TELEMETRY_OPTOUT"
tool="${1:-}"
[ $# -gt 0 ] && shift
case "$tool" in
  dotnet) exe="/mnt/c/Program Files/dotnet/dotnet.exe" ;;
  pwsh)   exe="/mnt/c/Program Files/PowerShell/7/pwsh.exe" ;;
  *) echo "usage: tools/r.sh dotnet|pwsh <args...>" >&2; exit 2 ;;
esac
"$exe" "$@" 2>&1 | tr -d '\r'
exit "${PIPESTATUS[0]}"
