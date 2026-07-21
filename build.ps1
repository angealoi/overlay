<#
.SYNOPSIS
    OsuEnlightenOverlay + Tablet Area Randomizer 한 번에 빌드 후 타임스탬프 폴더에 정리

.DESCRIPTION
    1. OsuEnlightenOverlay(.NET Framework 4.8 WinExe) 빌드
    2. Reconstructor / Tablet Area Randomizer(net8.0 라이브러리) 빌드
    3. build_YYYYMMDD_HHMMSS\ 타임스탬프 폴더 생성
    4. OsuEnlightenOverlay\ 서브폴더에 exe + OpenTK DLL + overlay-cursors\
    5. Tablet Area Randomizer\ 서브폴더에 dll + metadata.json
#>

$ErrorActionPreference = "Stop"
# $PSScriptRoot — 스크립트 파일 위치 기준 (CWD 영향 안 받음)
$solutionDir = $PSScriptRoot
if (-not $solutionDir) { $solutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$overlayProjectDir = Join-Path $solutionDir "OsuEnlightenOverlay"
$tabletProjectDir  = Join-Path $solutionDir "Reconstructor"

# 빌드 산출물이 떨어지는 실제 경로
$overlayReleaseDir = Join-Path $overlayProjectDir "bin\Release"
$tabletReleaseDir  = Join-Path $tabletProjectDir  "bin\Release\net8.0"

Write-Host "=== OsuEnlightenOverlay + Tablet Area Randomizer Build ===" -ForegroundColor Cyan

# 1. OsuEnlightenOverlay 빌드
Write-Host "[1/5] Building OsuEnlightenOverlay..." -ForegroundColor Yellow
Push-Location $solutionDir
dotnet build (Join-Path $overlayProjectDir "OsuEnlightenOverlay.csproj") -c Release --nologo -v q
$overlayExit = $LASTEXITCODE
Pop-Location
if ($overlayExit -ne 0) {
    Write-Host "OsuEnlightenOverlay BUILD FAILED (exit $overlayExit)" -ForegroundColor Red
    exit $overlayExit
}
Write-Host "OsuEnlightenOverlay build OK." -ForegroundColor Green

# 2. Tablet Area Randomizer 빌드
Write-Host "[2/5] Building Tablet Area Randomizer..." -ForegroundColor Yellow
Push-Location $solutionDir
dotnet build (Join-Path $tabletProjectDir "Reconstructor.csproj") -c Release --nologo -v q
$tabletExit = $LASTEXITCODE
Pop-Location
if ($tabletExit -ne 0) {
    Write-Host "Tablet Area Randomizer BUILD FAILED (exit $tabletExit)" -ForegroundColor Red
    exit $tabletExit
}
Write-Host "Tablet Area Randomizer build OK." -ForegroundColor Green

# 3. 타임스탬프 폴더 + 서브폴더 생성
$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$outDir      = Join-Path $solutionDir "build_$ts"
$overlayDir  = Join-Path $outDir "OsuEnlightenOverlay"
$tabletDir   = Join-Path $outDir "Tablet Area Randomizer"

New-Item -ItemType Directory -Path $overlayDir -Force | Out-Null
New-Item -ItemType Directory -Path $tabletDir  -Force | Out-Null
Write-Host "[3/5] Output folder: build_$ts" -ForegroundColor Yellow

# 4. OsuEnlightenOverlay 산출물 복사
Write-Host "[4/5] Copying OsuEnlightenOverlay files..." -ForegroundColor Yellow
$overlayFiles = @(
    "OsuEnlightenOverlay.exe",
    "OpenTK.dll",
    "OpenTK.GLControl.dll"
)
foreach ($f in $overlayFiles) {
    $src = Join-Path $overlayReleaseDir $f
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination $overlayDir -Force
        Write-Host "  $f" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: $f not found in build output" -ForegroundColor Red
    }
}

# overlay-cursors 빈 폴더 생성 (런타임에 캡처 이미지 저장용)
$cursorsDir = Join-Path $overlayDir "overlay-cursors"
New-Item -ItemType Directory -Path $cursorsDir -Force | Out-Null
Write-Host "  overlay-cursors\" -ForegroundColor Gray

# 5. Tablet Area Randomizer 산출물 복사
Write-Host "[5/5] Copying Tablet Area Randomizer files..." -ForegroundColor Yellow
$tabletDll = Join-Path $tabletReleaseDir "Tablet Area Randomizer.dll"
if (Test-Path -LiteralPath $tabletDll) {
    Copy-Item -LiteralPath $tabletDll -Destination $tabletDir -Force
    Write-Host "  Tablet Area Randomizer.dll" -ForegroundColor Gray
} else {
    Write-Host "  WARNING: Tablet Area Randomizer.dll not found in build output" -ForegroundColor Red
}

$tabletMetadata = Join-Path $tabletProjectDir "metadata.json"
if (Test-Path -LiteralPath $tabletMetadata) {
    Copy-Item -LiteralPath $tabletMetadata -Destination $tabletDir -Force
    Write-Host "  metadata.json" -ForegroundColor Gray
} else {
    Write-Host "  WARNING: metadata.json not found in source" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Output: $outDir" -ForegroundColor Cyan
Get-ChildItem $outDir -Recurse | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($outDir.Length + 1))" -ForegroundColor DarkGray
}
