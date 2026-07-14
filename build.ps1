<#
.SYNOPSIS
    OsuEnlightenOverlay 빌드 + 타임스탬프 폴더에 아웃풋 복사

.DESCRIPTION
    1. dotnet build -c Release
    2. build_YYYYMMDD_HHMMSS\ 폴더 생성
    3. 빌드 출력( exe, pdb, OpenTK DLL ) 복사
    4. overlay-cursors\ 빈 폴더 생성
#>

$ErrorActionPreference = "Stop"
# $PSScriptRoot — 스크립트 파일 위치 기준 (CWD 영향 안 받음)
$solutionDir = $PSScriptRoot
if (-not $solutionDir) { $solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
# 솔루션 폴더\OsuEnlightenOverlay\ = 프로젝트 폴더 (bin, Rendering 등이 여기 있음)
$projectDir  = Join-Path $solutionDir "OsuEnlightenOverlay"
$releaseDir  = Join-Path $projectDir "bin\Release"

Write-Host "=== OsuEnlightenOverlay Build ===" -ForegroundColor Cyan

# 1. 빌드
Write-Host "[1/4] Building..." -ForegroundColor Yellow
Push-Location $solutionDir
dotnet build OsuEnlightenOverlay.sln -c Release --nologo -v q
$buildExit = $LASTEXITCODE
Pop-Location
if ($buildExit -ne 0) {
    Write-Host "BUILD FAILED (exit $buildExit)" -ForegroundColor Red
    exit $buildExit
}
Write-Host "Build OK." -ForegroundColor Green

# 2. 타임스탬프 폴더
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$outDir = Join-Path $solutionDir "build_$ts"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Write-Host "[2/4] Output folder: build_$ts" -ForegroundColor Yellow

# 3. 빌드 출력 복사
Write-Host "[3/4] Copying build output..." -ForegroundColor Yellow
$buildFiles = @(
    "OsuEnlightenOverlay.exe",
    "OsuEnlightenOverlay.pdb",
    "OpenTK.dll",
    "OpenTK.GLControl.dll"
)
foreach ($f in $buildFiles) {
    $src = Join-Path $releaseDir $f
    if (Test-Path $src) {
        Copy-Item $src $outDir -Force
        Write-Host "  $f" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: $f not found in build output" -ForegroundColor Red
    }
}

# 4. overlay-cursors 빈 폴더 생성
$cursorsDir = Join-Path $outDir "overlay-cursors"
New-Item -ItemType Directory -Path $cursorsDir -Force | Out-Null
Write-Host "[4/4] Created overlay-cursors\" -ForegroundColor Yellow

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Output: $outDir" -ForegroundColor Cyan
Get-ChildItem $outDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName.Replace($outDir + '\',''))" -ForegroundColor DarkGray }