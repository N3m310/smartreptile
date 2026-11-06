<#
  Chay mobile E2E tren mot thiet bi, roi sinh bao cao.

  `flutter test` khong co `show-report`: no chi xuat duoc JSON tho qua `--file-reporter json:<file>`.
  Script nay chay suite, chuyen JSON do thanh:
    - integration_test/TEST-RESULTS.md  (giong web/e2e/TEST-RESULTS.md)
    - build/e2e/report.html             (dashboard, tu mo bang trinh duyet khi xong)
  Bao cao van duoc sinh ca khi suite fail - do la luc no can nhat.

  Vi du:
    .\tool\run_e2e.ps1
    .\tool\run_e2e.ps1 -Device gqyxmvzdyd4piffq
    .\tool\run_e2e.ps1 -ApiBase http://127.0.0.1:8080
    .\tool\run_e2e.ps1 -NoOpen            (chi sinh file, khong mo trinh duyet)
#>
param(
  [string]$Device = 'emulator-5554',
  [string]$ApiBase = 'http://10.0.2.2:8080',
  [string]$Out = 'integration_test/TEST-RESULTS.md',
  [string]$Html = 'build\e2e\report.html',
  [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'

$appDir = Split-Path -Parent $PSScriptRoot
Push-Location $appDir
try {
  # flutter: app/android/local.properties (flutter.sdk) truoc, roi moi den PATH - giong setup-mobile.ps1
  $flutter = $null
  $localProps = Join-Path $appDir 'android\local.properties'
  if (Test-Path $localProps) {
    $props = @{}
    foreach ($line in Get-Content $localProps) {
      if ($line -match '^\s*([^#=]+?)\s*=\s*(.*)$') {
        $props[$matches[1]] = $matches[2].Replace('\\', '\')
      }
    }
    if ($props['flutter.sdk']) {
      $candidate = Join-Path $props['flutter.sdk'] 'bin\flutter.bat'
      if (Test-Path $candidate) { $flutter = $candidate }
    }
  }
  if (-not $flutter) {
    $onPath = Get-Command flutter -ErrorAction SilentlyContinue
    if ($onPath) { $flutter = $onPath.Source }
  }
  if (-not $flutter) {
    throw 'Khong thay flutter (kiem tra flutter.sdk trong app/android/local.properties hoac PATH).'
  }

  $dart = Join-Path (Split-Path $flutter -Parent) 'dart.bat'
  if (-not (Test-Path $dart)) { $dart = 'dart' }

  $json = Join-Path $appDir 'build\e2e-report.json'
  New-Item -ItemType Directory -Force -Path (Split-Path $json -Parent) | Out-Null

  Write-Host "flutter : $flutter"
  Write-Host "device  : $Device"
  Write-Host "api     : $ApiBase"
  Write-Host ''

  & $flutter test integration_test/app_e2e_test.dart -d $Device `
    "--dart-define=API_BASE=$ApiBase" `
    --reporter compact `
    --file-reporter "json:$json"
  $testExit = $LASTEXITCODE

  # Bao cao van phai sinh ca khi suite fail - do chinh la luc no can nhat.
  & $dart run tool/e2e_report.dart $json --out $Out --html $Html
  $reportExit = $LASTEXITCODE
  if ($reportExit -ne 0) { Write-Warning 'Khong tao duoc bao cao Markdown/HTML.' }

  # Mo dashboard ngay khi build xong. `Start-Process` tren file .html se dung trinh duyet mac dinh.
  if (-not $NoOpen) {
    $htmlFull = Join-Path $appDir $Html
    if (Test-Path $htmlFull) {
      Write-Host "Mo dashboard: $htmlFull"
      Start-Process $htmlFull
    }
  }

  exit $testExit
}
finally {
  Pop-Location
}
