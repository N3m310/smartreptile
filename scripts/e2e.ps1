# E2E entry point for the Flutter app - docs/04-quality/05-e2e-ai-testing-plan.md section 8.
#
#   powershell -ExecutionPolicy Bypass -File scripts\e2e.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\e2e.ps1 -Suite smoke
#   powershell -ExecutionPolicy Bypass -File scripts\e2e.ps1 -Avd fcm_pixel_36
#
# Exit codes: 0 = every selected suite passed, 1 = a test failed, 2 = a prerequisite failed.
# Artifacts : build\e2e\<suite>.json and <suite>.log always; <suite>-fail.png and <suite>-logcat.txt on failure.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
# `ErrorActionPreference` stays at Continue because flutter writes Gradle/JVM warnings to stderr, and a native
# command's stderr becomes a terminating error under `Stop` - which aborted the first version on a JVM warning.
param(
    [ValidateSet('smoke', 'hermetic', 'fullstack', 'all')][string]$Suite = 'all',
    [string]$Avd = 'focus_study',
    [switch]$StopDeviceAtEnd
)

$ErrorActionPreference = 'Continue'

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'app'
$out = Join-Path $root 'build\e2e'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$emu = Join-Path $sdk 'emulator\emulator.exe'
$env:PATH = "$(Join-Path $sdk 'platform-tools');$env:PATH"

function Fail-Prerequisite([string]$Message) {
    Write-Host "PREREQUISITE: $Message" -ForegroundColor Red
    exit 2
}

function Wait-Until([scriptblock]$Probe, [int]$TimeoutSec, [string]$What) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $Probe) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

# ---- 1. Preflight -----------------------------------------------------------------------------------------------
Write-Host '== preflight' -ForegroundColor Cyan

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) { Fail-Prerequisite 'flutter is not on PATH' }
Write-Host ("  flutter : " + ((& flutter --version | Select-Object -First 1) -join ''))

if (-not (Test-Path $adb)) { Fail-Prerequisite "adb not found at $adb (install the Android SDK or set ANDROID_SDK_ROOT)" }
if (-not (Test-Path $emu)) { Fail-Prerequisite "emulator binary not found at $emu" }

$suites = @()
if ($Suite -eq 'all') { $suites = @('smoke', 'hermetic') } else { $suites = @($Suite) }

foreach ($name in $suites) {
    if ($name -eq 'fullstack') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            Fail-Prerequisite 'suite "fullstack" needs Docker Desktop, and `docker` is not on PATH'
        }
        $engine = (& docker version --format '{{.Server.Version}}' 2>$null)
        if (-not $engine) {
            Fail-Prerequisite 'Docker engine is not running - start Docker Desktop and run this again'
        }
    }
}

# ---- 2. Device --------------------------------------------------------------------------------------------------
Write-Host '== device' -ForegroundColor Cyan

function Get-DeviceSerial {
    foreach ($line in (& $adb devices)) {
        if ($line -match '^(emulator-\d+)\s+device') { return $matches[1] }
    }
    return $null
}

$serial = Get-DeviceSerial
if (-not $serial) {
    Write-Host "  booting $Avd headless ..."
    Start-Process -FilePath $emu -ArgumentList @(
        '-avd', $Avd, '-no-window', '-no-audio', '-no-snapshot-save', '-gpu', 'swiftshader_indirect'
    ) | Out-Null
    if (-not (Wait-Until { (& $adb shell getprop sys.boot_completed 2>$null) -match '1' } 300 "boot of $Avd")) {
        Fail-Prerequisite "emulator $Avd did not finish booting within 300 s"
    }
    $serial = Get-DeviceSerial
    if (-not $serial) { Fail-Prerequisite 'no emulator serial after boot' }
}
else {
    Wait-Until { (& $adb shell getprop sys.boot_completed 2>$null) -match '1' } 120 'sys.boot_completed' | Out-Null
}
Write-Host "  device  : $serial"

# ---- 2b. Backend, for the fullstack suite only ------------------------------------------------------------------
$stackStartedHere = $false
if ($suites -contains 'fullstack') {
    Write-Host '== backend' -ForegroundColor Cyan

    # Only start what is not already up, and only stop what this script started: a developer's own `docker compose
    # up` must survive a test run.
    $running = (& docker compose -f (Join-Path $root 'docker-compose.yml') ps -q api 2>$null)
    if (-not $running) {
        Push-Location $root
        & docker compose up -d --wait | Out-Host
        Pop-Location
        $stackStartedHere = $true
    }
    else {
        Write-Host '  compose stack already running - leaving it as it is'
    }

    $ready = Wait-Until {
        try { (Invoke-WebRequest 'http://127.0.0.1:8080/health/ready' -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 }
        catch { $false }
    } 180 '/health/ready == 200'
    if (-not $ready) {
        if ($stackStartedHere) {
            Push-Location $root
            & docker compose down | Out-Host
            Pop-Location
        }
        Fail-Prerequisite 'the API never reported /health/ready as 200'
    }
    Write-Host '  api     : http://127.0.0.1:8080 (healthy)'
}

# ---- 3. Suites --------------------------------------------------------------------------------------------------
$results = @()
Push-Location $app

& flutter pub get | Out-Host
& flutter gen-l10n | Out-Host

foreach ($name in $suites) {
    Write-Host "== suite: $name" -ForegroundColor Cyan

    # The refresh token lives in the keychain, so a previous run's session would skip the login screen.
    & $adb -s $serial shell pm clear com.smartreptile.smart_reptile | Out-Null

    # Each suite's defines are declared here so a reader can see what it runs against (plan section 3).
    $defines = @()
    if ($name -eq 'smoke') { $defines = @() }
    if ($name -eq 'hermetic') { $defines = @('API_BASE=http://127.0.0.1:18999') }
    if ($name -eq 'fullstack') { $defines = @('API_BASE=http://10.0.2.2:8080') }

    $report = Join-Path $out "$name.json"
    $log = Join-Path $out "$name.log"
    # A suite may be several files: `fullstack` covers the keeper's journeys and the alert path. Each file is a
    # separate `flutter test` run so one crash cannot hide the other's result, and each keeps its own report.
    $targets = @("integration_test/e2e_${name}_test.dart")
    if ($name -eq 'fullstack') { $targets += 'integration_test/e2e_alerts_test.dart' }

    foreach ($target in $targets) {
        $slug = [System.IO.Path]::GetFileNameWithoutExtension($target) -replace '^e2e_', '' -replace '_test$', ''
        $fileReport = Join-Path $out "$slug.json"
        $fileLog = Join-Path $out "$slug.log"

        # Stale failure evidence from an earlier run must not survive a green one: a screenshot with no failing test
        # next to it is worse than no screenshot. Keyed on the file's own slug, which is what the summary prints.
        foreach ($stale in @("$slug-fail.png", "$slug-logcat.txt")) {
            $stalePath = Join-Path $out $stale
            if (Test-Path $stalePath) { Remove-Item $stalePath -Force }
        }

        $arguments = @('test', $target, '-d', $serial)
        foreach ($define in $defines) { $arguments += "--dart-define=$define" }
        $arguments += @('--file-reporter', "json:$fileReport")

        $started = Get-Date
        & flutter @arguments 2>&1 | Tee-Object -FilePath $fileLog | Out-Host
        $code = $LASTEXITCODE
        $duration = [int]((Get-Date) - $started).TotalSeconds

        if ($code -ne 0) {
            & $adb -s $serial exec-out screencap -p > (Join-Path $out "$slug-fail.png")
            # Bounded on purpose: a full `logcat -d` is ~27 MB, and nobody reads past the last few seconds of it.
            & $adb -s $serial logcat -d -t 2000 > (Join-Path $out "$slug-logcat.txt")
        }

        $results += [pscustomobject]@{ Suite = $slug; Exit = $code; Seconds = $duration; Report = $fileReport }
    }
}

Pop-Location

if ($stackStartedHere) {
    Write-Host '== tearing the stack down (this run started it)' -ForegroundColor Cyan
    Push-Location $root
    & docker compose down | Out-Host
    Pop-Location
}

if ($StopDeviceAtEnd -and $serial) { & $adb -s $serial emu kill | Out-Null }

# ---- 4. Summary -------------------------------------------------------------------------------------------------
Write-Host ''
$results | Format-Table -AutoSize | Out-Host

$failed = @($results | Where-Object { $_.Exit -ne 0 })
if ($failed.Count -gt 0) {
    Write-Host ("FAILED: " + (($failed | ForEach-Object { $_.Suite }) -join ', ')) -ForegroundColor Red
    exit 1
}

Write-Host 'OK' -ForegroundColor Green
exit 0
