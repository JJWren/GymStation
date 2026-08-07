<#
.SYNOPSIS
    Registers (or re-registers) the scheduled task that resets the GymStation
    test stack (round 4.5). Idempotent.

.PARAMETER Cadence
    Daily (default) or Weekly.

.PARAMETER At
    Time of day to run. Default 04:00.

.PARAMETER StackDir
    Passed through to reset-test-stack.ps1. Default Z:\docker\gymstation-test.

.EXAMPLE
    pwsh -File scripts\register-test-reset-task.ps1
    pwsh -File scripts\register-test-reset-task.ps1 -Cadence Weekly -At 03:30
#>
[CmdletBinding()]
param(
    [ValidateSet('Daily', 'Weekly')]
    [string]$Cadence = 'Daily',
    [string]$At = '04:00',
    [string]$StackDir = 'Z:\docker\gymstation-test'
)

$ErrorActionPreference = 'Stop'

$taskName = 'GymStation test-stack reset'
$resetScript = Join-Path $PSScriptRoot 'reset-test-stack.ps1'
if (-not (Test-Path $resetScript)) { throw "reset-test-stack.ps1 not found beside this script." }

# Prefer pwsh (PowerShell 7); fall back to Windows PowerShell.
$shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $shell) { $shell = (Get-Command powershell).Source }

$action = New-ScheduledTaskAction -Execute $shell `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$resetScript`" -StackDir `"$StackDir`""

$trigger = if ($Cadence -eq 'Weekly') {
    New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At $At
} else {
    New-ScheduledTaskTrigger -Daily -At $At
}

$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd -RunOnlyIfNetworkAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Resets the GymStation test stack to a fresh standard seed (round 4.5).' -Force | Out-Null

Write-Host "Registered '$taskName' — $Cadence at $At, targeting $StackDir."
Write-Host "Run on demand: Start-ScheduledTask -TaskName '$taskName'"
Write-Host "Change cadence in Task Scheduler, or re-run this script with -Cadence/-At."
