param([switch]$WithDatabase)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & "$PSScriptRoot/scan-secrets.ps1" -SelfTest
    & "$PSScriptRoot/scan-secrets.ps1"
    dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
    dotnet build --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test --no-build -c Release --filter 'Category!=Database' --logger trx
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed' }
    dotnet run --no-build -c Release --project src/StockMon.Worker -- fake-once
    if ($LASTEXITCODE -ne 0) { throw 'Fake smoke test failed' }
    $before = $env:Monitor__Mode
    try {
        $env:Monitor__Mode = 'Live'
        dotnet run --no-build -c Release --project src/StockMon.Worker -- fake-once
        if ($LASTEXITCODE -ne 2) { throw 'Unimplemented live mode was not rejected' }
    } finally { $env:Monitor__Mode = $before }
    $previousDsnFile = $env:STOCKMON_MIGRATION_DSN_FILE
    try {
        $env:STOCKMON_MIGRATION_DSN_FILE = ''
        dotnet run --no-build -c Release --project src/StockMon.Worker -- migrate
        if ($LASTEXITCODE -ne 2) { throw 'Missing migration secret was not rejected' }
    } finally { $env:STOCKMON_MIGRATION_DSN_FILE = $previousDsnFile }
    if ($WithDatabase) { & "$PSScriptRoot/test-db.ps1" }
    else { Write-Output 'Database integration NOT RUN; use -WithDatabase on a Docker host.' }
} finally { Pop-Location }
# Expected negative CLI tests return 2; do not leak that code to the CI shell.
$global:LASTEXITCODE = 0
