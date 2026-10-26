[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Publish,
    [string]$DotNetPath = 'dotnet',
    [string]$OutputDirectory = 'dist/LocalChat'
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command $DotNetPath -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK not found. Install it or pass -DotNetPath <path-to-dotnet.exe>.'
}
Push-Location $PSScriptRoot
try {
    & $DotNetPath build LocalChat.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    # This project is a console assertion runner, not a dotnet test adapter.
    if ($Test) {
        & $DotNetPath run --project tests/LocalChat.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    if ($Publish) {
        & $DotNetPath publish src/LocalChat -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    }
}
finally {
    Pop-Location
}
