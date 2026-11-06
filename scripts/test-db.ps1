$ErrorActionPreference = 'Stop'
$container = 'stockmon-test-' + [Guid]::NewGuid().ToString('N')
$previousPassword = $env:POSTGRES_PASSWORD
$previousDsn = $env:STOCKMON_TEST_ADMIN_DSN
$started = $false
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $env:POSTGRES_PASSWORD = $password
    docker run --detach --name $container --publish '127.0.0.1::5432' --env POSTGRES_PASSWORD --env POSTGRES_DB=stockmon_test 'postgres:16-bookworm@sha256:5264710be20f409750ac6dc4e5706dcef929c87e241605463f0f9aefa932a8d9' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL test container failed to start' }
    $started = $true
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        docker exec $container pg_isready -U postgres -d stockmon_test *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'PostgreSQL readiness timeout' }
    $binding = docker port $container 5432/tcp
    $port = ($binding.Trim() -split ':')[-1]
    $env:STOCKMON_TEST_ADMIN_DSN = "Host=127.0.0.1;Port=$port;Database=stockmon_test;Username=postgres;Password=$password;Include Error Detail=false"
    dotnet test --no-build -c Release --filter 'Category=Database' --logger trx
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL integration tests failed' }
} finally {
    if ($started) { docker rm --force --volumes $container | Out-Null }
    $env:POSTGRES_PASSWORD = $previousPassword
    $env:STOCKMON_TEST_ADMIN_DSN = $previousDsn
    Pop-Location
}
