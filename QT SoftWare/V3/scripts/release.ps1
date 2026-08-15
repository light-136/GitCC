# release.ps1 —— V3 发布：configure(Release) + build + ctest + windeployqt + 打包 zip
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 [-SkipTests]
# 产出：deploy/ 目录（可双击运行）+ DataScope-V3-<版本>-win64.zip（发布包）
# 满足：DoD R3（windeployqt 配置期硬失败）、R4（deploy 产物 --version/--selftest）、
#       R5（verify_deploy.ps1 DLL 完整性）、R6（deploy 不入 git + 带版本号 zip）。

param(
    [switch]$SkipTests   # 调试用：跳过 ctest，仅构建+部署
)

$ErrorActionPreference = "Stop"

# ---- 固化工具链路径（本机唯一正确组合，与 build.ps1 同源）----
$QtDir      = "D:/Qt/5.12.2/mingw73_64"
$MingwDir   = "D:/Qt/Tools/mingw730_64"
$CmakeExe   = "C:/Users/lenovo/AppData/Roaming/Python/Python310/site-packages/cmake/data/bin/cmake.exe"
$NinjaExe   = "C:/Users/lenovo/AppData/Roaming/Python/Python310/Scripts/ninja.exe"
$CtestExe   = "C:/Users/lenovo/AppData/Roaming/Python/Python310/site-packages/cmake/data/bin/ctest.exe"
$SrcDir     = Split-Path -Parent $PSScriptRoot   # V3 根目录

$buildType = "Release"
$buildDir  = "$SrcDir/build_release"
$deployDir = "$SrcDir/deploy"

# MinGW 7.3.0 优先于系统 MSYS2 g++（ABI 必须匹配 Qt 5.12.2）
$env:PATH = "$MingwDir\bin;$env:PATH"

Write-Host "=== 0. 清理旧发布产物 ==="
if (Test-Path $deployDir) { Remove-Item $deployDir -Recurse -Force }

Write-Host "=== 1. configure ($buildType) ==="
& $CmakeExe -S $SrcDir -B $buildDir -G Ninja `
    -DCMAKE_MAKE_PROGRAM="$NinjaExe" `
    -DCMAKE_CXX_COMPILER="$MingwDir/bin/g++.exe" `
    -DCMAKE_RC_COMPILER="$MingwDir/bin/windres.exe" `
    -DCMAKE_PREFIX_PATH="$QtDir" `
    "-DCMAKE_BUILD_TYPE=$buildType"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== 2. build ==="
& $CmakeExe --build $buildDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    Write-Host "=== 3. ctest（unit + integration + e2e 探针）==="
    $env:PATH = "$QtDir\bin;$MingwDir\bin;$env:PATH"
    & $CtestExe --test-dir $buildDir --output-on-failure
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "=== 4. windeployqt 部署（复制 exe 后按依赖部署 Qt DLL + 插件）==="
New-Item -ItemType Directory -Force $deployDir | Out-Null
Copy-Item "$buildDir/DataScope.exe" $deployDir
Copy-Item "$buildDir/simulator.exe" $deployDir
& "$QtDir/bin/windeployqt.exe" --release "$deployDir/DataScope.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$QtDir/bin/windeployqt.exe" --release "$deployDir/simulator.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# MinGW C++ 运行时 DLL（windeployqt 不复制，需手动补）
Copy-Item "$MingwDir/bin/libgcc_s_seh-1.dll" $deployDir
Copy-Item "$MingwDir/bin/libstdc++-6.dll"    $deployDir
Copy-Item "$MingwDir/bin/libwinpthread-1.dll" $deployDir

Write-Host "=== 5. 部署完整性门禁（DoD R5）==="
& (Join-Path $PSScriptRoot "verify_deploy.ps1") -DeployDir $deployDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== 6. deploy 产物运行时冒烟（--version / --selftest，DoD R4）==="
# simulator 是 console 程序，& 能直接捕获 stdout
$simVer = & "$deployDir/simulator.exe" --version 2>&1
if ($LASTEXITCODE -ne 0) { Write-Error "simulator --version 失败"; exit 1 }
Write-Host "  simulator : $simVer"

# DataScope 是 WIN32 GUI 程序，PowerShell 的 & 不为它建立 stdout 管道（捕获为空）；
# 改用 Start-Process 显式重定向 stdout，才能读到 --selftest 的输出并断言。
$dsOut = Join-Path $env:TEMP "datascope_selftest.txt"
$dsErr = Join-Path $env:TEMP "datascope_selftest_err.txt"
Remove-Item $dsOut, $dsErr -Force -ErrorAction SilentlyContinue
$dsProc = Start-Process -FilePath "$deployDir/DataScope.exe" -ArgumentList "--selftest" `
    -NoNewWindow -Wait -PassThru -RedirectStandardOutput $dsOut -RedirectStandardError $dsErr
if ($dsProc.ExitCode -ne 0) {
    Write-Error "DataScope --selftest 失败（退出码 $($dsProc.ExitCode)）"
    Get-Content $dsErr -ErrorAction SilentlyContinue | ForEach-Object { Write-Error $_ }
    exit 1
}
$dsSelf = Get-Content $dsOut -Raw -ErrorAction SilentlyContinue
if ($dsSelf -notmatch "SELFTEST OK") {
    Write-Error "DataScope --selftest 输出不含 'SELFTEST OK'：[$dsSelf]"
    exit 1
}
Write-Host "  DataScope : $($dsSelf.Trim())"

Write-Host "=== 7. buildinfo.txt（构建记录，ADR-10）==="
# 版本号从 configure_file 生成的 version.h 读（保证单一版本源）
$versionH = Get-Content "$buildDir/generated/version.h" -Raw
$version  = [regex]::Match($versionH, '"([^"]+)"').Groups[1].Value

$buildInfo = @"
产品:        DataScope Studio V3
版本:        $version
构建类型:    $buildType
构建时间:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
工具链:      Qt 5.12.2 (mingw73_64) + MinGW 7.3.0 posix-seh + Ninja + CMake
编译器:      g++ 7.3.0 (posix-seh, C++17)
构建机器:    $env:COMPUTERNAME
"@
$buildInfo | Out-File -FilePath (Join-Path $deployDir "buildinfo.txt") -Encoding utf8

Write-Host "=== 8. 打包带版本号 zip ==="
$zipName = "DataScope-V3-$version-win64.zip"
$zipPath = Join-Path $SrcDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $deployDir "*") -DestinationPath $zipPath

Write-Host ""
Write-Host "=== 发布完成 ==="
Write-Host "  发布目录: $deployDir"
Write-Host "  发布包:   $zipPath"
Write-Host "  版本:     $version"
exit 0
