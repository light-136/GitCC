# SmartMES.Native C++ DLL 编译脚本
# 需要安装 CMake 和 Visual Studio Build Tools（或 MinGW）
# 运行方式：在 SmartMES.Native 目录执行 .\build.ps1

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir   = "$ScriptDir\build"
$OutputDir  = "$ScriptDir\..\SmartMES.UI\bin\Debug\net9.0-windows"

Write-Host "[SmartMES.Native] 开始编译 C++ DLL..." -ForegroundColor Cyan

# 创建构建目录
if (-not (Test-Path $BuildDir)) {
    New-Item -ItemType Directory -Path $BuildDir | Out-Null
}

# 检查CMake
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] 未找到CMake，请安装CMake: https://cmake.org/download/" -ForegroundColor Red
    exit 1
}

# CMake 配置
Write-Host "[1/3] CMake 配置..." -ForegroundColor Yellow
push-location $BuildDir
cmake .. -DCMAKE_BUILD_TYPE=Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] CMake配置失败" -ForegroundColor Red
    pop-location; exit 1
}

# 编译
Write-Host "[2/3] 编译中..." -ForegroundColor Yellow
cmake --build . --config Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] 编译失败" -ForegroundColor Red
    pop-location; exit 1
}
pop-location

# 复制DLL到输出目录
Write-Host "[3/3] 复制DLL到UI输出目录..." -ForegroundColor Yellow
$dllPath = Get-ChildItem -Path $BuildDir -Recurse -Filter "SmartMES.Native.dll" | Select-Object -First 1
if ($dllPath) {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    Copy-Item -Path $dllPath.FullName -Destination "$OutputDir\SmartMES.Native.dll" -Force
    Write-Host "[OK] DLL已复制到: $OutputDir\SmartMES.Native.dll" -ForegroundColor Green
} else {
    Write-Host "[WARN] 未找到编译输出的DLL文件" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host " C++ Native DLL 编译完成！" -ForegroundColor Green
Write-Host " 在SmartMES UI的 'C++ Native' 页面" -ForegroundColor Green
Write-Host " 点击'检测DLL版本'验证调用" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Cyan
