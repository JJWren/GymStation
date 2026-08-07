<#
.SYNOPSIS
    Resets the GymStation TEST stack to a fresh standard seed (round 4.5).

.DESCRIPTION
    Pulls the latest release image, drops the test database volume, brings the
    stack back up (the app migrates itself on boot), then reseeds the standard
    300-person tenant via /ops/seed-standard.

    SAFETY: refuses to run against any compose file whose postgres service is not
    named exactly 'gymstation-test-db'. The live lab (gymstation-db) is out of
    reach by construction - this script cannot be pointed at it.

.PARAMETER StackDir
    The test stack directory (holds compose.yaml + .env). Default Z:\docker\gymstation-test.

.PARAMETER SkipPull
    Skip 'compose pull' (reuse the local image) - faster for content iteration.
#>
[CmdletBinding()]
param(
    [string]$StackDir = 'Z:\docker\gymstation-test',
    [switch]$SkipPull
)

$ErrorActionPreference = 'Stop'

$composePath = Join-Path $StackDir 'compose.yaml'
$envPath = Join-Path $StackDir '.env'
$script:logPath = $null

# Falls back to console-only when the log file never became usable - a bad
# StackDir must still produce a visible failure and a non-zero exit.
function Log([string]$msg) {
    $line = "{0:u}  {1}" -f (Get-Date), $msg
    if ($script:logPath) {
        try { $line | Tee-Object -FilePath $script:logPath -Append; return } catch { }
    }
    Write-Host $line
}

try {
    $logDir = Join-Path $StackDir 'logs'
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $script:logPath = Join-Path $logDir ("reset-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))

    if (-not (Test-Path $composePath)) { throw "No compose.yaml in $StackDir." }

    # --- SAFETY INTERLOCK: this must be the TEST stack, never the lab ---
    # The magic name must sit INSIDE the gymstation-test-db service block, and
    # that block must be a postgres image - a stray matching line elsewhere in
    # the file is not good enough.
    $composeText = Get-Content $composePath -Raw
    $blockMatch = [regex]::Match($composeText, '(?m)^\s{2}gymstation-test-db:\s*$((?:\r?\n(?!\s{0,2}\S).*)*)')
    if (-not $blockMatch.Success `
        -or $blockMatch.Groups[1].Value -notmatch 'container_name:\s*gymstation-test-db' `
        -or $blockMatch.Groups[1].Value -notmatch 'image:\s*postgres') {
        throw "REFUSING: $composePath has no postgres service 'gymstation-test-db' declaring that container_name. This script only resets the test stack - never the lab."
    }
    if ($composeText -match '(?m)^\s*container_name:\s*gymstation-db\s*$') {
        throw "REFUSING: $composePath references the LIVE lab container 'gymstation-db'. Aborting."
    }
    Log "Safety check passed - target is the test stack."

    # The stack rides an EXTERNAL network so companion containers (pgAdmin)
    # survive resets. Idempotent - and `network ls` exits zero whether or not
    # the network exists, so a missing network and an unreachable daemon are
    # cleanly distinguishable (review round 1).
    $existingNetwork = docker network ls --filter 'name=^gymstation-test-net$' --format '{{.Name}}'
    if ($LASTEXITCODE -ne 0) {
        throw "docker network ls failed - is the Docker daemon running?"
    }
    if ($existingNetwork -ne 'gymstation-test-net') {
        Log "Creating external network gymstation-test-net..."
        $null = docker network create gymstation-test-net
        if ($LASTEXITCODE -ne 0) { throw "Could not create the gymstation-test-net network." }
    }

    # --- secrets from the stack .env ---
    if (-not (Test-Path $envPath)) { throw "No .env in $StackDir (needs OPS_API_KEY, SEED_PASSWORD, POSTGRES_PASSWORD)." }
    $envVars = @{}
    foreach ($raw in Get-Content $envPath) {
        $line = $raw.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $k, $v = $line.Split('=', 2)
            $envVars[$k.Trim()] = $v.Trim()
        }
    }
    $opsKey = $envVars['OPS_API_KEY']
    $seedPassword = $envVars['SEED_PASSWORD']
    $slug = if ($envVars['SEED_SLUG']) { $envVars['SEED_SLUG'] } else { 'testworks' }
    if (-not $opsKey -or -not $seedPassword) { throw "OPS_API_KEY and SEED_PASSWORD must be set in $envPath." }
    if ($seedPassword.Length -lt 10) { throw "SEED_PASSWORD must be at least 10 characters." }

    Push-Location $StackDir
    try {
        if (-not $SkipPull) {
            Log "Pulling latest image..."
            docker compose pull; if ($LASTEXITCODE) { throw "compose pull failed." }
        }
        Log "Dropping the stack and its volume..."
        docker compose down -v; if ($LASTEXITCODE) { throw "compose down failed." }
        Log "Bringing the stack up (app migrates on boot)..."
        docker compose up -d; if ($LASTEXITCODE) { throw "compose up failed." }

        # The app binds its port only AFTER MigrateOnStart finishes, so HTTP
        # readiness from the host is the true "schema is up" signal (the runtime
        # image ships no curl/wget for an in-container healthcheck to use).
        Log "Waiting for the app to answer on :8630 (migrations run first)..."
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Seconds 5
            try {
                $ping = Invoke-WebRequest -Uri 'http://localhost:8630/login' -UseBasicParsing -TimeoutSec 5
                if ($ping.StatusCode -eq 200) { $ready = $true; break }
            } catch { }
        }
        if (-not $ready) { throw "gymstation-test-web did not answer on :8630 in time." }

        Log "Seeding the standard tenant ($slug)..."
        $body = @{ slug = $slug; seedPassword = $seedPassword } | ConvertTo-Json
        $headers = @{ 'X-Ops-Key' = $opsKey; 'Content-Type' = 'application/json' }
        $resp = Invoke-RestMethod -Method Post -Uri 'http://localhost:8630/ops/seed-standard' -Headers $headers -Body $body
        Log "Seeded: gym $($resp.gymId), $($resp.logins) logins activated."

        Log "Probing the public page..."
        $probe = Invoke-WebRequest -Uri "http://localhost:8630/$slug" -UseBasicParsing
        if ($probe.StatusCode -ne 200) { throw "Public page returned $($probe.StatusCode)." }

        Log "RESET COMPLETE - /$slug is live on :8630 with $($resp.logins) sign-in-able accounts."
    }
    finally {
        Pop-Location
    }
}
catch {
    Log "FAILED: $($_.Exception.Message)"
    exit 1
}
