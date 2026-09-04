param(
    [Parameter(Mandatory = $true)]
    [string]$ServerInstance,

    [string]$DatabaseName = 'SGRNetGuard',

    [ValidateSet('Windows', 'Sql')]
    [string]$Authentication = 'Windows',

    [string]$SqlUser,

    [string]$SqlPassword,

    [string[]]$ScriptFiles = @(
        (Join-Path $PSScriptRoot '01_schema.sql'),
        (Join-Path $PSScriptRoot '02_seed_sites.sql'),
        (Join-Path $PSScriptRoot '03_dashboard_upgrade.sql'),
        (Join-Path $PSScriptRoot '04_device_detail_upgrade.sql'),
        (Join-Path $PSScriptRoot '05_device_detail_timestamp_upgrade.sql'),
        (Join-Path $PSScriptRoot '06_device_identity_upgrade.sql'),
        (Join-Path $PSScriptRoot '07_last_known_region_upgrade.sql')
    )
)

function New-ConnectionString {
    param(
        [string]$Server,
        [string]$Database,
        [string]$AuthMode,
        [string]$User,
        [string]$Password
    )

    if ($AuthMode -eq 'Windows') {
        return "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=8;"
    }

    if ([string]::IsNullOrWhiteSpace($User) -or [string]::IsNullOrWhiteSpace($Password)) {
        throw 'SQL authentication requires -SqlUser and -SqlPassword.'
    }

    return "Server=$Server;Database=$Database;User Id=$User;Password=$Password;TrustServerCertificate=True;Encrypt=False;Connection Timeout=8;"
}

function Invoke-SqlScript {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Path
    )

    $sql = Get-Content -Path $Path -Raw
    $batches = [System.Text.RegularExpressions.Regex]::Split($sql, '(?im)^\s*GO\s*(?:--.*)?$')

    foreach ($batch in $batches) {
        if ([string]::IsNullOrWhiteSpace($batch)) {
            continue
        }

        $command = $Connection.CreateCommand()
        $command.CommandTimeout = 180
        $command.CommandText = $batch
        [void]$command.ExecuteNonQuery()
    }
}

$connectionString = New-ConnectionString -Server $ServerInstance -Database 'master' -AuthMode $Authentication -User $SqlUser -Password $SqlPassword

Write-Host "Using server: $ServerInstance"
Write-Host "Authentication: $Authentication"
Write-Host "Scripts:"
$ScriptFiles | ForEach-Object { Write-Host " - $_" }

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
try {
    $connection.Open()
    Write-Host 'Connection OK.'

    foreach ($script in $ScriptFiles) {
        if (-not (Test-Path $script)) {
            throw "Script not found: $script"
        }

        Write-Host "Running $script ..."
        Invoke-SqlScript -Connection $connection -Path $script
        Write-Host "Done $script"
    }

    Write-Host 'All database scripts completed successfully.'
}
finally {
    $connection.Dispose()
}