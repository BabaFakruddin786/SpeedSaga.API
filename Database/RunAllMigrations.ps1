# Applies SpeedSagaDB base schema + all Updates_*.sql in order.
# Usage (from repo root or Database folder):
#   .\Database\RunAllMigrations.ps1
#   .\Database\RunAllMigrations.ps1 -Server "localhost\SQLEXPRESS01" -Database SpeedSagaDB

param(
    [string]$Server = "localhost\SQLEXPRESS01",
    [string]$Database = "SpeedSagaDB",
    [switch]$UpdatesOnly
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Invoke-SqlFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        Write-Warning "Skip missing: $Path"
        return
    }
    Write-Host "Running $(Split-Path $Path -Leaf) ..."
    & sqlcmd -S $Server -d $Database -E -b -i $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Failed: $(Split-Path $Path -Leaf) (exit $LASTEXITCODE)"
    }
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Error "sqlcmd not found. Install SQL Server command-line tools or run scripts manually in SSMS."
    exit 1
}

Write-Host "Target: $Server / $Database"
Write-Host ""

$base = Join-Path $here "SpeedSagaDB.sql"
if (-not (Test-Path $base)) {
    Write-Error "Missing SpeedSagaDB.sql"
    exit 1
}

# Create database if needed (SpeedSagaDB.sql starts with USE SpeedSagaDB)
Write-Host "Ensuring database exists..."
& sqlcmd -S $Server -E -Q "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];"
if ($LASTEXITCODE -ne 0) { throw "Could not create database" }

if (-not $UpdatesOnly) {
    Invoke-SqlFile $base
} else {
    Write-Host "Skipping SpeedSagaDB.sql (-UpdatesOnly)"
}

$updates = Get-ChildItem -Path $here -Filter "Updates_*.sql" | Sort-Object Name
foreach ($f in $updates) {
    Invoke-SqlFile $f.FullName
}

Write-Host ""
Write-Host "All migrations applied successfully."
