# build.ps1 —— V3 一键 configure + build（本机零远程依赖）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 [-Release]
# 固化工具链路径，消除手抄差异；后续 release.ps1 在此基础上扩展 test/package。

param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"

# ---- 固化工具链路径（本机唯一正确组合）----
$QtDir      = "D:/Qt/5.12.2/mingw73_64"
$MingwDir   = "D:/Qt/Tools/mingw730_64"
$CmakeExe   = "C:/Users/lenovo/AppData/Roaming/Python/Python310/site-packages/cmake/data/bin/cmake.exe"
$NinjaExe   = "C:/Users/lenovo/AppData/Roaming/Python/Python310/Scripts/ninja.exe"
$SrcDir     = Split-Path -Parent $PSScriptRoot   # V3 根目录

# MinGW 7.3.0 优先于系统 MSYS2 g++（ABI 必须匹配 Qt 5.12.2）
$env:PATH = "$MingwDir\bin;$env:PATH"

$buildType = "Debug"
$buildDir  = "build"
if ($Release) {
    $buildType = "Release"
    $buildDir  = "build_release"
}

Write-Host "=== configure ($buildType) ==="
& $CmakeExe -S $SrcDir -B "$SrcDir/$buildDir" -G Ninja `
    -DCMAKE_MAKE_PROGRAM="$NinjaExe" `
    -DCMAKE_CXX_COMPILER="$MingwDir/bin/g++.exe" `
    -DCMAKE_RC_COMPILER="$MingwDir/bin/windres.exe" `
    -DCMAKE_PREFIX_PATH="$QtDir" `
    "-DCMAKE_BUILD_TYPE=$buildType"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== build ==="
& $CmakeExe --build "$SrcDir/$buildDir"
exit $LASTEXITCODE
