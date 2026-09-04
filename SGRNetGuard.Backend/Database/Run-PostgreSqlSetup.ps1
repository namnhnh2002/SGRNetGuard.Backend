param(
    [Parameter(Mandatory = $true)] [string]$ConnectionString,
    [string]$PsqlPath = 'psql'
)

$scriptRoot = $PSScriptRoot
& $PsqlPath $ConnectionString -v ON_ERROR_STOP=1 -f (Join-Path $scriptRoot 'postgresql_schema.sql')
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL schema setup failed.' }
& $PsqlPath $ConnectionString -v ON_ERROR_STOP=1 -f (Join-Path $scriptRoot 'postgresql_seed.sql')
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL seed setup failed.' }
Write-Host 'PostgreSQL schema and seed setup completed.'