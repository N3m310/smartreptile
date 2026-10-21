# Creates a NEW account, species profile and enclosure through the API, for a live demo.
#
# This file is deliberately NOT part of the database seed (`DemoDataSeeder`). The seeder fills the tables a screen
# needs to have something in it; this script exists for the opposite moment -- showing that a person can create
# those things themselves, with their own names, and watching the server assign the ids. Nothing here is
# pre-inserted, so every row it produces is the result of a real request, which is what a demo is trying to prove.
#
# Run it while the API is up (run-local.bat starts it on :8080):
#     powershell -ExecutionPolicy Bypass -File scripts\demo-create.ps1
#
# Optional arguments:
#     -ApiBase          http://localhost:8080
#     -AdminUsername    admin                (needs `species.write`, so the species profile can be authored)
#     -AdminPassword    Admin#Password2026!
#     -SpeciesSuffix    <timestamp>          (override to make the run reproducible)
#
# Every literal below is ASCII on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a Vietnamese
# character in this file would reach the console as mojibake. The data the API stores is UTF-8 and unaffected, and
# the profile this script creates is named in English so the demo works in both UI languages.
param(
    [string]$ApiBase = 'http://localhost:8080',
    [string]$AdminUsername = 'admin',
    [string]$AdminPassword = 'Admin#Password2026!',
    [string]$SpeciesSuffix = ''
)

$ErrorActionPreference = 'Stop'
$api = "$($ApiBase.TrimEnd('/'))/api/v1"

if ([string]::IsNullOrWhiteSpace($SpeciesSuffix)) {
    $SpeciesSuffix = Get-Date -Format 'yyyyMMdd-HHmmss'
}

function Invoke-Api {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Token,
        $Body
    )

    $headers = @{ Accept = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $parameters = @{
        Method      = $Method
        Uri         = "$api$Path"
        Headers     = $headers
        ContentType = 'application/json; charset=utf-8'
    }

    if ($null -ne $Body) {
        # Depth 10: a band list is an array of objects of optional members, which the default depth of 2 truncates.
        $parameters['Body'] = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }

    return Invoke-RestMethod @parameters
}

function Show-Step([string]$Text) {
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor Cyan
}

Write-Host "Demo create against $api" -ForegroundColor Gray

# ---- 1. The administrator, who is the only account allowed to author the catalogue -------------------------------
Show-Step "Signing in as $AdminUsername"
$admin = Invoke-Api -Method Post -Path '/auth/login' -Body @{ usernameOrEmail = $AdminUsername; password = $AdminPassword }
$adminToken = $admin.accessToken
Write-Host "  ok: role $($admin.user.role)"

# ---- 2. A species profile ---------------------------------------------------------------------------------------
# One band per family, both valid: a temperature band for the animal and a humidity band for the enclosure. The API
# validates the pair as a whole (BR-10.2), so this is also a demonstration that the payload shape is right.
Show-Step 'Creating a species profile'
$speciesName = "Demo species $SpeciesSuffix"
$species = Invoke-Api -Method Post -Path '/species-profiles' -Token $adminToken -Body @{
    name             = $speciesName
    scientificName   = 'Eublepharis macularius'
    climateZone      = 'SemiArid'
    photoperiodHours = 12
    lightsOnLocalTime = '07:00'
    lightThresholdLux = 800
    minLightHoursPerDay = 9
    notes            = 'Created by scripts/demo-create.ps1 -- safe to delete.'
    bands            = @(
        @{
            metric      = 'tempC'
            phase       = 'day'
            targetMin   = 26
            targetMax   = 31
            dwellWarnMinutes = 5
            dwellCritMinutes = 2
            recoveryMargin   = 0.5
        },
        @{
            metric      = 'humidityPct'
            phase       = 'any'
            targetMin   = 40
            targetMax   = 65
            dwellWarnMinutes = 15
            dwellCritMinutes = 3
            recoveryMargin   = 2
        }
    )
}

Write-Host "  ok: $($species.name)  id=$($species.id)  builtIn=$($species.isBuiltIn)  canEdit=$($species.canEdit)"

# ---- 3. A new account -------------------------------------------------------------------------------------------
# Registration is the public path and always produces an Owner -- the role that may create enclosures. The email
# domain is `.test`, which cannot receive mail: this account is not meant to outlive the demo.
Show-Step 'Registering an account'
$accountName = "demo.guest.$SpeciesSuffix".ToLowerInvariant()
$registration = Invoke-Api -Method Post -Path '/auth/register' -Body @{
    username = $accountName
    email    = "$accountName@example.test"
    password = 'Demo!Reptile1'
}

Write-Host "  ok: $accountName registered (status $($registration.status))"
Write-Host "  recovery code: $($registration.recoveryCode)  <- shown once, and only here"

Show-Step "Signing in as $accountName"
$guest = Invoke-Api -Method Post -Path '/auth/login' -Body @{ usernameOrEmail = $accountName; password = 'Demo!Reptile1' }
$guestToken = $guest.accessToken
Write-Host "  ok: role $($guest.user.role)"

# ---- 4. An enclosure, on the profile created above ---------------------------------------------------------------
Show-Step 'Creating an enclosure for the new account, on the new species profile'
$terrarium = Invoke-Api -Method Post -Path '/terrariums' -Token $guestToken -Body @{
    name             = "Demo enclosure $SpeciesSuffix"
    speciesProfileId = $species.id
    location         = 'Demo corner'
    description      = 'Created by scripts/demo-create.ps1 -- safe to delete.'
}

Write-Host "  ok: $($terrarium.name)  id=$($terrarium.id)  species=$($terrarium.speciesName)"

# ---- 5. Prove it is readable back, from the account that owns it -------------------------------------------------
Show-Step 'Reading the new account''s list back'
$list = Invoke-Api -Method Get -Path '/terrariums' -Token $guestToken
Write-Host "  items: $($list.items.Count)"
foreach ($item in $list.items) {
    Write-Host "   - $($item.name)  species=$($item.speciesName)  device=$($item.devicePublicId)"
}

# ---- 6. What the demo would do next ------------------------------------------------------------------------------
Write-Host ''
Write-Host 'Next, live, in the UI:' -ForegroundColor Green
Write-Host "  1. Sign in as $accountName / Demo!Reptile1"
Write-Host '  2. Open the enclosure, add a threshold override, and watch the effective-thresholds table change source'
Write-Host "  3. Sign back in as $AdminUsername and delete the enclosure, the profile and the account"
Write-Host ''
Write-Host 'Cleanup (order matters -- a species profile refuses to be deleted while any enclosure, even a'
Write-Host 'soft-deleted one, still points at it, so the enclosure has to go first):'
Write-Host "  DELETE /api/v1/admin/terrariums/$($terrarium.id)       (as $AdminUsername -- hard delete)"
Write-Host "  DELETE /api/v1/species-profiles/$($species.id)         (as $AdminUsername)"
Write-Host '  DELETE /api/v1/users/{id}                             (as admin; a hard delete that also'
Write-Host '                                                          purges anything the account still owns)'
