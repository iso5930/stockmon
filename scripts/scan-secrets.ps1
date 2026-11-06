param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$patterns = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '\bgh[pousr]_[A-Za-z0-9]{36,}\b',
    '\bgithub_pat_[A-Za-z0-9_]{40,}\b',
    '\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}\b',
    '\b[0-9]{8,12}:[A-Za-z0-9_-]{30,}\b',
    '(?i)(?:api[_-]?key|app[_-]?secret|access[_-]?token|account[_-]?number)\s*["'']?\s*[:=]\s*["''][A-Za-z0-9_-]{8,}["'']'
)
function Has-Secret([string]$Text) {
    foreach ($pattern in $patterns) { if ($Text -match $pattern) { return $true } }
    return $false
}
if ($SelfTest) {
    if (-not (Has-Secret ('gh' + 'p_' + ('A' * 36)))) { throw 'Secret scanner self-test failed' }
    if (Has-Secret 'SYNTH-ACCOUNT') { throw 'Secret scanner false positive' }
    Write-Output 'Secret scanner self-test passed'
    return
}
$root = Split-Path $PSScriptRoot -Parent
$forbidden = '(?i)(^|/)(secrets|data)/|(^|/)\.env($|\.)|\.(pem|pfx|key|dump|bak)$|appsettings\.Local\.json$'
$failed = $false
# Keep Unicode paths readable; inspection includes new files before staging.
$paths = (& git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files' }
foreach ($path in $paths) {
    if ($path -match $forbidden -and $path -notmatch '\.env\.example$') {
        Write-Error "Forbidden repository path: $path" -ErrorAction Continue
        $failed = $true
        continue
    }
    $full = Join-Path $root $path
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        if (Has-Secret ([System.IO.File]::ReadAllText($full))) {
            Write-Error "Possible secret in $path (value suppressed)" -ErrorAction Continue
            $failed = $true
        }
    }
}
if ($failed) { throw 'Secret scan failed; inspect locally without copying values into logs.' }
Write-Output 'Repository secret-pattern scan passed (not a guarantee against all sensitive data).'
