param([switch]$SkipDemo)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settings = Get-Content -LiteralPath (Join-Path $projectRoot 'SkinRag.Api/appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$connectionText = $settings.ConnectionStrings.DefaultConnection
if ($env:ConnectionStrings__DefaultConnection) { $connectionText = $env:ConnectionStrings__DefaultConnection }
$connection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionText)
if (-not $connection.IntegratedSecurity) {
    throw 'This helper uses Windows authentication. Run the SQL scripts using your approved SQL credentials for other authentication methods.'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'sqlcmd is required.' }
$sqlArgs = @('-S', $connection.DataSource, '-d', $connection.InitialCatalog, '-E', '-b', '-l', '15', '-f', '65001')
if ($connection.TrustServerCertificate) { $sqlArgs += '-C' }
& sqlcmd @sqlArgs -i (Join-Path $PSScriptRoot 'upgrade.sql')
if ($LASTEXITCODE -ne 0) { throw 'Schema upgrade failed. Demo data was not applied.' }
if (-not $SkipDemo) {
    & sqlcmd @sqlArgs -i (Join-Path $PSScriptRoot 'seed-demo.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Demo seed failed.' }
}
Write-Host 'Catalog upgrade completed.'
