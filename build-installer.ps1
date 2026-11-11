param(
    [string]$Version = '0.45.0',
    [string]$Makensis
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$' -or
    @($Version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count -gt 0) {
    throw 'Specify a three-part version, for example 0.1.0.'
}

if ($Makensis) {
    $Makensis = (Resolve-Path -LiteralPath $Makensis).Path
} else {
    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($command) {
        $Makensis = $command.Source
    } else {
        $Makensis = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
            Where-Object { $_ } |
            ForEach-Object { Join-Path $_ 'NSIS\makensis.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
    }
}

if (-not $Makensis) { throw 'Install NSIS 3 or pass the path to makensis.exe with -Makensis.' }

Push-Location -LiteralPath $PSScriptRoot
try {
    & .\build.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

    New-Item -ItemType Directory -Path .\dist -Force | Out-Null
    & $Makensis /INPUTCHARSET UTF8 "/DAPP_VERSION=$Version" .\installer.nsi
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    Get-Item -LiteralPath .\dist\ExileRoute-Setup.exe
} finally {
    Pop-Location
}
