# Dumps every table in the SmartReptile database, with its columns and its rows, as Markdown.
#
# Written as a script rather than typed by hand so the snapshot can be regenerated (and so the redaction rule is
# one edit away from changing everywhere). Secrets are never printed: a hash or salt column is replaced by its
# byte length, which is what the project's own audit-trail rule does with device secrets ("never recorded, only
# that a rotation happened").
#
# Every literal below is ASCII on purpose. Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a non-ASCII
# character in this file would reach the output as mojibake (an em dash became "a-euro-quote" the first time);
# the *data* is unaffected, because it goes through .NET strings and is written as UTF-8 without a BOM.
param(
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$Database = 'SmartReptile',
    [string]$RepoRoot = 'c:\Users\drago\Desktop\Studies\PRM393\smartreptile'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data

# The connection string comes from .env, like every other script here: no credential is written down twice.
$map = @{}
Get-Content (Join-Path $RepoRoot '.env') | Where-Object { $_ -match '^\s*[A-Za-z_][A-Za-z0-9_]*=' } | ForEach-Object {
    $i = $_.IndexOf('=')
    $map[$_.Substring(0, $i).Trim()] = $_.Substring($i + 1).Trim().Trim('"')
}
$connectionString = ($map['DB_CONNECTION_STRING'] -replace 'Database=[^;]*', "Database=$Database")

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

function Invoke-Query([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $table = New-Object System.Data.DataTable
    $table.Load($command.ExecuteReader())
    # `,` stops PowerShell from unrolling the DataTable into its rows on the way out of the function: an array of
    # DataRows has no `.Rows` and no `.Select()`, which is exactly how this broke the first time.
    return , $table
}

# Columns whose *values* must never leave the database, whatever the table. Matched case-insensitively.
$secretPattern = 'hash$|salt$|rowversion$|secret$|^token$|password$'

function Format-Cell($value, [bool]$isSecret) {
    if ($value -is [DBNull]) { return '`NULL`' }
    if ($isSecret) {
        $length = if ($value -is [byte[]]) { $value.Length } else { ([string]$value).Length }
        return "``<redacted: $length bytes>``"
    }
    if ($value -is [byte[]]) { return "``<binary: $($value.Length) bytes>``" }
    if ($value -is [DateTimeOffset]) { return $value.UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss') + 'Z' }
    if ($value -is [DateTime]) { return $value.ToString('yyyy-MM-dd HH:mm:ss') }
    if ($value -is [bool]) { return $(if ($value) { 'true' } else { 'false' }) }
    $text = [string]$value
    if ($text -eq '') { return '`NULL`' }
    return (($text -replace '\|', '\|') -replace '(\r?\n)+', ' ')
}

$tables = Invoke-Query @"
SELECT t.name AS TableName, SUM(p.rows) AS [RowCount]
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
GROUP BY t.name
ORDER BY t.name
"@

$columns = Invoke-Query @"
SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE,
       CASE WHEN EXISTS (
           SELECT 1 FROM sys.index_columns ic
           JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE i.is_primary_key = 1
             AND ic.object_id = OBJECT_ID(c.TABLE_NAME)
             AND ic.column_id = COLUMNPROPERTY(OBJECT_ID(c.TABLE_NAME), c.COLUMN_NAME, 'ColumnId')
       ) THEN 1 ELSE 0 END AS IsPrimaryKey,
       CASE WHEN EXISTS (
           SELECT 1 FROM sys.identity_columns idc
           WHERE idc.object_id = OBJECT_ID(c.TABLE_NAME) AND idc.name = c.COLUMN_NAME
       ) THEN 1 ELSE 0 END AS IsIdentity
FROM INFORMATION_SCHEMA.COLUMNS c
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
"@

$stamp = (Get-Date).ToUniversalTime()
$lines = New-Object System.Collections.Generic.List[string]

$lines.Add("# Database contents snapshot - $Database")
$lines.Add('')
$lines.Add("**Taken $($stamp.ToString('yyyy-MM-dd HH:mm:ss')) UTC** from the local SQL Server 2022 container")
$lines.Add("(``127.0.0.1,14330``) through ``scripts/dump-database.ps1``. It is a **point-in-time snapshot of a")
$lines.Add("development database**, not the schema's source of truth - that is ``07-appendices/02`` - and the")
$lines.Add('database moves as soon as anything writes to it.')
$lines.Add('')
$lines.Add('**Secrets are not printed.** Every column matching `hash`, `salt`, `rowversion` or `secret` shows its')
$lines.Add('length instead of its value (secrets are never recorded, only that they were written). Binary columns')
$lines.Add('that are not secrets show their byte count, and `NULL` is the SQL `NULL`, which is different from an')
$lines.Add('empty string.')
$lines.Add('')
$lines.Add('## Tables and row counts')
$lines.Add('')
$lines.Add('| Table | Rows |')
$lines.Add('|---|---|')
$total = 0
foreach ($row in $tables.Rows) {
    $lines.Add("| ``$($row.TableName)`` | $($row.RowCount) |")
    $total += [int]$row.RowCount
}
$lines.Add("| **total** | **$total** |")
$lines.Add('')

# Several closed vocabularies are stored as integers. The enums in `backend/src/SmartReptile.Domain/` are the
# source of truth; the mapping is repeated here because `Metric = 1` tells a reader nothing on its own.
$lines.Add('## Reading the integer columns')
$lines.Add('')
$lines.Add('The values are the C# enums in `backend/src/SmartReptile.Domain/` - the source of truth - repeated here so')
$lines.Add('the tables below can be read without opening the code:')
$lines.Add('')
$lines.Add('| Column | Enum (file) | Values |')
$lines.Add('|---|---|---|')
$lines.Add('| `Metric` | `MetricCode` (`Metrics/MetricDictionary.cs`) | 1 TempC, 2 HumidityPct, 3 LightLux, 4 UvIndex, 5 SurfaceTempC, 6 BatteryPct, 7 RssiDbm |')
$lines.Add('| `Phase` | `ThresholdPhase` (`Thresholds/ThresholdEnums.cs`) | 0 Any, 1 Day, 2 Night |')
$lines.Add('| `ClimateZone` | `ClimateZone` (`Species/SpeciesProfile.cs`) | 0 Tropical, 1 SemiArid, 2 Arid, 3 Temperate |')
$lines.Add('| `Role` | `UserRole` (`Identity/User.cs`) | 0 Owner, 1 Technician, 2 Viewer |')
$lines.Add('| `Severity`, `MinNotifySeverity` | `AlertSeverity` (`Alerts/Alert.cs`) | 0 Info, 1 Warning, 2 Critical |')
$lines.Add('| `State` | `AlertState` (`Alerts/Alert.cs`) | 0 Open, 1 Acknowledged, 2 Resolved |')
$lines.Add('| `Status` (`Device`) | `DeviceStatus` (`Devices/Device.cs`) | 0 Provisioning, 1 Online, 2 Offline, 3 Revoked, 4 Maintenance |')
$lines.Add('| `Protocol` | `DeviceProtocol` (`Devices/Device.cs`) | 0 Mqtt, 1 HttpFallback |')
$lines.Add('| `Source` (`TelemetrySample`) | `IngestSource` (`Readings/TelemetrySample.cs`) | 0 Mqtt, 1 HttpFallback, 2 Seed |')
$lines.Add('| `QualityFlags` | bitmask `QualityFlags` (`Readings/QualityFlags.cs`) | 1 SensorFault, 2 Implausible, 4 FirstAfterBoot, 8 Backfilled, 16 ClockUnsynced, 32 CalibrationApplied |')
$lines.Add('')
$lines.Add('`0` is a clean sample. Only `1` (sensor fault) and `2` (implausible) exclude a reading from evaluation')
$lines.Add('(`QualityRules.NotEvaluable`), so a flag of `16` means *the device clock was not NTP-synced*: those samples')
$lines.Add('are still evaluated, which is why the API answers with a status for them instead of `Unavailable`. Note that')
$lines.Add('`status` is not stored anywhere - it is computed at read time from the effective band, per ADR-005.')
$lines.Add('')

foreach ($row in $tables.Rows) {
    $table = [string]$row.TableName
    $columnsOfTable = $columns.Select("TABLE_NAME = '$table'")
    $lines.Add("## ``$table`` - $($row.RowCount) row(s)")
    $lines.Add('')

    $lines.Add('| Column | Type | Null | Keys |')
    $lines.Add('|---|---|---|---|')
    foreach ($column in $columnsOfTable) {
        $type = [string]$column.DATA_TYPE
        if ($column.CHARACTER_MAXIMUM_LENGTH -isnot [DBNull]) {
            $length = [int]$column.CHARACTER_MAXIMUM_LENGTH
            $type += if ($length -eq -1) { '(max)' } else { "($length)" }
        }
        $keys = @()
        if ([int]$column.IsPrimaryKey -eq 1) { $keys += 'PK' }
        if ([int]$column.IsIdentity -eq 1) { $keys += 'identity' }
        $nullability = if ([string]$column.IS_NULLABLE -eq 'YES') { 'yes' } else { 'no' }
        $lines.Add("| ``$($column.COLUMN_NAME)`` | ``$type`` | $nullability | $($keys -join ', ') |")
    }
    $lines.Add('')

    $data = Invoke-Query "SELECT * FROM [$table]"
    if ($data.Rows.Count -eq 0) {
        $lines.Add('_No rows._')
        $lines.Add('')
        continue
    }

    $names = $data.Columns | ForEach-Object { $_.ColumnName }
    $lines.Add('| ' + ($names -join ' | ') + ' |')
    $lines.Add('|' + (($names | ForEach-Object { '---' }) -join '|') + '|')
    foreach ($dataRow in $data.Rows) {
        $cells = foreach ($column in $data.Columns) {
            Format-Cell $dataRow[$column.ColumnName] ($column.ColumnName -match $secretPattern)
        }
        $lines.Add('| ' + ($cells -join ' | ') + ' |')
    }
    $lines.Add('')
}

$connection.Close()

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($OutFile, ($lines -join "`n") + "`n", $utf8)
Write-Output "wrote $OutFile ($($lines.Count) lines, $($tables.Rows.Count) tables, $total rows)"
