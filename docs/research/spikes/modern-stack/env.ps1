# Process-scoped isolation only; nothing persisted to user profile / registry / PATH.
$T = 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-modernstack'
$env:DOTNET_ROOT = "$T\dotnet"
$env:PATH = "$T\dotnet;" + $env:PATH
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_HOME = "$T\clihome"
$env:NUGET_PACKAGES = "$T\nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = "$T\nuget\http"
$env:NUGET_PLUGINS_CACHE_PATH = "$T\nuget\plugins"
$env:NUGET_SCRATCH = "$T\nuget\scratch"
$env:TEMP = "$T\tmp"; $env:TMP = "$T\tmp"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:UseSharedCompilation = 'false'
Set-Location "$T\src"
