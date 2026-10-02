# build/publish.ps1 — single source of truth for release publish flags.
# Keep README "Build from source" in sync with this file.
param([string]$Output = (Join-Path $PSScriptRoot '..\publish'))
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot '..\src\YTMTaskbarWidget\YTMTaskbarWidget.csproj'

dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -o $Output

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
