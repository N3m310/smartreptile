#requires -Version 5.1
<#
  setup-mobile.ps1 - chuan bi moi truong chay app SmartReptile tren
  DIEN THOAI (USB) va EMULATOR.

  - Tu tim SDK Android / adb / emulator / flutter tu app\android\local.properties
  - Bat emulator neu can, doi boot xong
  - adb reverse tcp:8080 tcp:8080 cho TAT CA thiet bi (dien thoai + emulator)
  - Kiem tra backend /health/ready
  - (tuy chon) build 1 lan roi cai + mo app tren ca hai may

  Vi du:
    .\setup-mobile.ps1
    .\setup-mobile.ps1 -StartEmulator -Run phone
    .\setup-mobile.ps1 -Run emulator
    .\setup-mobile.ps1 -Backend -StartEmulator -Run both `
        -TurnUrl turn:192.168.1.20:3478 -TurnUser demo -TurnPass demo

  Vi sao ca hai may deu dung 127.0.0.1: vi `adb reverse` chuyen cong 8080
  cua TUNG thiet bi ve backend tren PC, nen mot APK chay duoc tren ca hai.
#>
[CmdletBinding()]
param(
  [string]$Emulator = 'focus_study',
  [switch]$StartEmulator,
  [string]$PhoneSerial = '',
  [ValidateSet('none', 'phone', 'emulator', 'both')]
  [string]$Run = 'none',
  [string]$TurnUrl = '',
  [string]$TurnUser = '',
  [string]$TurnPass = '',
  [int]$ApiPort = 8080,
  [switch]$Backend,
  [switch]$ColdBoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir   = Join-Path $repoRoot 'app'
$pkg      = 'com.smartreptile.smart_reptile'

function Say([string]$msg, [string]$color = 'Cyan') {
  Write-Host $msg -ForegroundColor $color
}

function Read-Properties([string]$path) {
  $map = @{}
  foreach ($line in Get-Content -LiteralPath $path) {
    if ($line.Trim() -eq '') { continue }
    if ($line -match '^\s*[#!]') { continue }
    $i = $line.IndexOf('=')
    if ($i -lt 1) { continue }
    $k = $line.Substring(0, $i).Trim()
    $v = $line.Substring($i + 1).Trim().Replace('\\', '\')
    $map[$k] = $v
  }
  return $map
}

# ---------- 1. Duong dan SDK / adb / emulator / flutter ----------

$localProps = Join-Path $appDir 'android\local.properties'
if (-not (Test-Path $localProps)) { throw "Khong thay file: $localProps" }
$props = Read-Properties $localProps

$sdk = $props['sdk.dir']
if (-not $sdk) { $sdk = $env:ANDROID_HOME }
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) { $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path $sdk)) { throw "Khong thay Android SDK: $sdk" }

$adb = Join-Path $sdk 'platform-tools\adb.exe'
$emu = Join-Path $sdk 'emulator\emulator.exe'
if (-not (Test-Path $adb)) { throw "Khong thay adb: $adb" }

$flutter = $null
if ($props['flutter.sdk']) {
  $candidate = Join-Path $props['flutter.sdk'] 'bin\flutter.bat'
  if (Test-Path $candidate) { $flutter = $candidate }
}
if (-not $flutter) {
  $onPath = Get-Command flutter -ErrorAction SilentlyContinue
  if ($onPath) { $flutter = $onPath.Source }
}
if (-not $flutter) { throw 'Khong thay flutter (kiem tra flutter.sdk trong local.properties hoac PATH).' }

Say "SDK     : $sdk"
Say "adb     : $adb"
Say "flutter : $flutter"
Say ''

# Native tools write progress/warnings to stderr; with ErrorActionPreference=Stop that
# becomes a terminating error, so every adb call goes through this wrapper.
function Invoke-Adb([string[]]$adbArgs) {
  $previous = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $adb @adbArgs 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previous
  }
}

Invoke-Adb @('start-server') | Out-Null

# ---------- 2. Ham tien ich ----------

function Get-Devices {
  $valid = @('device', 'offline', 'unauthorized', 'recovery', 'sideload', 'bootloader', 'no permissions')
  $list = @()
  foreach ($line in ((Invoke-Adb @('devices')).Output -split "`r?`n" | Select-Object -Skip 1)) {
    $parts = $line -split '\s+', 2
    if ($parts.Count -lt 2) { continue }
    $serial = $parts[0].Trim()
    $state  = $parts[1].Trim()
    if (-not $serial) { continue }
    if ($valid -notcontains $state) { continue }
    $list += [pscustomobject]@{ Serial = $serial; State = $state }
  }
  return $list
}

function Get-EmulatorSerial {
  $found = @(Get-Devices) | Where-Object { $_.Serial -like 'emulator-*' } | Select-Object -First 1
  if ($found) { return $found.Serial }
  return $null
}

function Wait-Boot([string]$serial, [int]$timeoutSec = 300) {
  $end = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $end) {
    $state = (Invoke-Adb @('-s', $serial, 'get-state')).Output.Trim()
    if ($state -eq 'device') {
      $boot = (Invoke-Adb @('-s', $serial, 'shell', 'getprop', 'sys.boot_completed')).Output.Trim()
      if ($boot -eq '1') { return $true }
    }
    Start-Sleep -Seconds 2
  }
  return $false
}

function Set-Reverse([string]$serial, [int]$port) {
  Invoke-Adb @('-s', $serial, 'reverse', "tcp:$port", "tcp:$port") | Out-Null
  $listed = (Invoke-Adb @('-s', $serial, 'reverse', '--list')).Output
  return [bool]($listed -match "tcp:$port")
}

function Test-Backend([int]$port) {
  try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health/ready" -UseBasicParsing -TimeoutSec 4
    return ($r.StatusCode -eq 200)
  } catch { return $false }
}

# ---------- 3. Bat emulator ----------

if ($StartEmulator) {
  $running = Get-EmulatorSerial
  if ($running) {
    Say "Emulator da chay: $running" 'Green'
  } else {
    if (-not (Test-Path $emu)) { throw "Khong thay emulator.exe: $emu" }
    $avds = & $emu -list-avds
    if ($avds -notcontains $Emulator) {
      Say "AVD '$Emulator' khong ton tai. Cac AVD co san:" 'Yellow'
      foreach ($a in $avds) { Say "  - $a" }
      throw 'Chay lai voi -Emulator <ten-avd>.'
    }
    $emuArgs = @('-avd', $Emulator)
    if ($ColdBoot) { $emuArgs += '-no-snapshot-load' }
    Say "Dang bat emulator $Emulator ..." 'Cyan'
    Start-Process -FilePath $emu -ArgumentList $emuArgs | Out-Null
    $end = (Get-Date).AddSeconds(240)
    while ((Get-Date) -lt $end -and -not (Get-EmulatorSerial)) { Start-Sleep -Seconds 3 }
    $running = Get-EmulatorSerial
    if (-not $running) {
      throw 'Emulator khong khoi dong duoc. Thu: emulator -avd <ten> -verbose ; emulator-check.exe accel'
    }
    Say "Emulator da len: $running" 'Green'
  }
  if (-not (Wait-Boot $running 300)) { throw "Emulator $running chua boot xong sau 300s." }
  Say 'Emulator boot xong.' 'Green'
  Say ''
}

# ---------- 4. Backend (tuy chon) ----------

if ($Backend -and -not (Test-Backend $ApiPort)) {
  if (Get-Command docker -ErrorAction SilentlyContinue) {
    Say 'Dang bat SQL Server (docker compose up -d db)...' 'Cyan'
    Push-Location $repoRoot
    try { & docker compose up -d db | Out-Null } catch { Say 'Bo qua docker db.' 'Yellow' }
    Pop-Location
  }
  Say 'Dang chay backend trong cua so moi (dotnet run)...' 'Cyan'
  Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/k', 'dotnet run --project src/SmartReptile.Api' `
    -WorkingDirectory (Join-Path $repoRoot 'backend') | Out-Null
  $end = (Get-Date).AddSeconds(180)
  while ((Get-Date) -lt $end -and -not (Test-Backend $ApiPort)) { Start-Sleep -Seconds 3 }
}

# ---------- 5. Liet ke thiet bi + adb reverse ----------

$devices = @(Get-Devices)
if ($devices.Count -eq 0) {
  Say 'Khong thay thiet bi nao. Kiem tra: cap USB, USB debugging, hop thoai "Allow".' 'Yellow'
}

$phone = $PhoneSerial
if (-not $phone) {
  $first = $devices | Where-Object { $_.Serial -notlike 'emulator-*' -and $_.State -eq 'device' } | Select-Object -First 1
  if ($first) { $phone = $first.Serial }
}

Say 'Thiet bi / adb reverse:' 'Cyan'
foreach ($d in $devices) {
  $kind = 'dien thoai'
  if ($d.Serial -like 'emulator-*') { $kind = 'emulator' }
  if ($d.State -ne 'device') {
    Say ("  {0,-22} {1,-10} {2,-12} -> BO QUA (chua o trang thai device)" -f $d.Serial, $kind, $d.State) 'Yellow'
    continue
  }
  if (Set-Reverse $d.Serial $ApiPort) {
    Say ("  {0,-22} {1,-10} reverse tcp:{2} -> OK" -f $d.Serial, $kind, $ApiPort) 'Green'
  } else {
    Say ("  {0,-22} {1,-10} reverse -> THAT BAI" -f $d.Serial, $kind) 'Red'
  }
}
Say ''

if (Test-Backend $ApiPort) {
  Say "Backend http://127.0.0.1:$ApiPort -> Healthy" 'Green'
} else {
  Say "Backend CHUA san sang. Chay tay: cd backend ; dotnet run --project src/SmartReptile.Api" 'Yellow'
  Say 'Hoac them -Backend de script tu bat.' 'Yellow'
}
Say ''

# ---------- 6. Chay app (tuy chon) ----------

$apiBase = "http://127.0.0.1:$ApiPort"
$defines = @("--dart-define=API_BASE=$apiBase")
if ($TurnUrl) {
  $defines += "--dart-define=TURN_URL=$TurnUrl"
  if ($TurnUser) { $defines += "--dart-define=TURN_USERNAME=$TurnUser" }
  if ($TurnPass) { $defines += "--dart-define=TURN_PASSWORD=$TurnPass" }
}

function Start-Flutter([string]$serial) {
  Push-Location $appDir
  try { & $flutter run -d $serial @defines } finally { Pop-Location }
}

switch ($Run) {
  'phone' {
    if (-not $phone) { throw 'Khong thay dien thoai nao o trang thai device.' }
    Start-Flutter $phone
  }
  'emulator' {
    $e = Get-EmulatorSerial
    if (-not $e) { throw 'Emulator chua chay. Them -StartEmulator hoac bat emulator truoc.' }
    Start-Flutter $e
  }
  'both' {
    $targets = @($devices | Where-Object { $_.State -eq 'device' } | Select-Object -ExpandProperty Serial)
    if ($targets.Count -lt 2) {
      Say ("Chi thay {0} thiet bi dang o trang thai device: {1}" -f $targets.Count, ($(if ($targets.Count) { $targets -join ', ' } else { 'khong co' }))) 'Yellow'
      throw 'Can it nhat 2 thiet bi cho -Run both (1 dien thoai + 1 emulator).'
    }
    Say "Build APK debug mot lan (API_BASE=$apiBase)..." 'Cyan'
    Push-Location $appDir
    try {
      & $flutter build apk --debug @defines
      if ($LASTEXITCODE -ne 0) { throw "flutter build apk that bai (exit $LASTEXITCODE)." }
      $apk = Join-Path $appDir 'build\app\outputs\flutter-apk\app-debug.apk'
      if (-not (Test-Path $apk)) { throw "Khong thay APK: $apk" }
      foreach ($t in $targets) {
        Say "Cai len $t ..." 'Cyan'
        $install = Invoke-Adb @('-s', $t, 'install', '-r', $apk)
        if ($install.ExitCode -ne 0 -or $install.Output -notmatch 'Success') {
          Say ("  cai len {0} that bai: {1}" -f $t, $install.Output) 'Yellow'
          continue
        }
        Say '  Cai xong, dang mo app...' 'Green'
        Invoke-Adb @('-s', $t, 'shell', 'am', 'start', '-n', "$pkg/.MainActivity") | Out-Null
      }
    } finally { Pop-Location }
    Say 'Da cai + mo app tren tat ca thiet bi (khong co hot reload; dung -Run phone/-Run emulator neu can).' 'Green'
  }
  'none' {
    Say 'Cac lenh chay app (moi lenh o mot cua so rieng):' 'Cyan'
    if ($phone) { Say "  flutter run -d $phone $($defines -join ' ')" }
    $e = Get-EmulatorSerial
    if ($e) { Say "  flutter run -d $e $($defines -join ' ')" }
    Say ''
    Say 'Camera: dien thoai -> Terrarium -> Camera truc tiep -> icon camera -> chon be -> Bat camera.' 'Cyan'
    Say '        emulator  -> Camera truc tiep -> tab WebRTC -> chon cung be.' 'Cyan'
    Say '        Trang thai may phat phai la: "Dang phat toi <viewerId>."' 'Cyan'
  }
}
