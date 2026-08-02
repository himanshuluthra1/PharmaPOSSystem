#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes PharmaPOS and builds a Windows installer (Inno Setup).

.DESCRIPTION
  1) Prepares a masters-only SQL backup from your LocalDB PharmaPosDb (unless -SkipDistPrepare)
  2) Publishes a self-contained win-x64 build
  3) Compiles the Inno Setup script into artifacts\installer\

.PARAMETER SkipDistPrepare
  Skip rebuilding the master backup (use existing artifacts\dist\PharmaPosDb_Master.bak).

.PARAMETER SkipPublish
  Skip dotnet publish (use existing artifacts\publish\win-x64).
#>
param(
    [switch]$SkipDistPrepare,
    [switch]$SkipPublish,
    [string]$Configuration = "Release",
    [string]$SourceConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publishDir = Join-Path $root "artifacts\publish\win-x64"
$distDir = Join-Path $root "artifacts\dist"
$installerDir = Join-Path $root "artifacts\installer"
$iss = Join-Path $root "installer\PharmaPOS.iss"

New-Item -ItemType Directory -Force -Path $publishDir, $distDir, $installerDir | Out-Null

if (-not $SkipDistPrepare) {
    Write-Host "==> Preparing masters-only distribution database..." -ForegroundColor Cyan
    dotnet run --project (Join-Path $root "tools\PharmaPOS.DistPrepare\PharmaPOS.DistPrepare.csproj") -c $Configuration -- `
        $SourceConnectionString $distDir
    if ($LASTEXITCODE -ne 0) { throw "DistPrepare failed with exit code $LASTEXITCODE" }
}
else {
    $bak = Join-Path $distDir "PharmaPosDb_Master.bak"
    if (-not (Test-Path $bak)) {
        throw "Missing $bak. Run without -SkipDistPrepare first."
    }
}

if (-not $SkipPublish) {
    Write-Host "==> Publishing self-contained win-x64..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root "src\PharmaPOS.WPF\PharmaPOS.WPF.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

    # Customer builds must not seed demo patients/stock; restore master bak instead.
    $prodSettings = @{
        ConnectionStrings = @{
            PharmaPosDb = "Server=(localdb)\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
        }
        App = @{
            SessionTimeoutMinutes = 30
            Theme = "Light"
            SeedSampleData = $false
            RestoreMasterBackupOnFirstRun = $true
            MasterBackupFileName = "PharmaPosDb_Master.bak"
        }
    } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $publishDir "appsettings.json") -Value $prodSettings -Encoding UTF8

    # Link (don't duplicate) the ~400MB master backup into publish\Data.
    $dataOut = Join-Path $publishDir "Data"
    New-Item -ItemType Directory -Force -Path $dataOut | Out-Null
    $bakSrc = Join-Path $distDir "PharmaPosDb_Master.bak"
    $metaSrc = Join-Path $distDir "PharmaPosDb_Master.meta.json"
    $bakDst = Join-Path $dataOut "PharmaPosDb_Master.bak"
    $metaDst = Join-Path $dataOut "PharmaPosDb_Master.meta.json"
    foreach ($pair in @(@($bakSrc, $bakDst), @($metaSrc, $metaDst))) {
        if (Test-Path $pair[1]) { Remove-Item $pair[1] -Force }
        try {
            New-Item -ItemType HardLink -Path $pair[1] -Target $pair[0] | Out-Null
        }
        catch {
            Write-Warning "HardLink failed for $($pair[0]); copying instead (needs free disk)."
            Copy-Item $pair[0] $pair[1] -Force
        }
    }
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 not found. Published app + master DB are ready under:" -ForegroundColor Yellow
    Write-Host "  $publishDir"
    Write-Host "  $distDir"
    Write-Host "Install Inno Setup from https://jrsoftware.org/isinfo.php then re-run with -SkipDistPrepare -SkipPublish"
    exit 0
}

Write-Host "==> Compiling installer..." -ForegroundColor Cyan
& $iscc "/DPublishDir=$publishDir" "/DDistDataDir=$distDir" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Installer created in: $installerDir" -ForegroundColor Green
Get-ChildItem $installerDir -Filter "PharmaPOS-Setup-*.exe" | ForEach-Object { Write-Host "  $($_.FullName) ($([math]::Round($_.Length/1MB,1)) MB)" }
