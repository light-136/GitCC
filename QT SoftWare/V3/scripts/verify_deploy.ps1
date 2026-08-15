# verify_deploy.ps1 —— V3 部署完整性门禁（DoD R5）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify_deploy.ps1 [-DeployDir <路径>]
# 判据：关键 exe + Qt 运行时 DLL + 平台插件 + MinGW 运行时 DLL，缺一个就非零退出。
# 说明：Release 构建的 Qt DLL 无 "d" 后缀（Qt5Core.dll 而非 Qt5Cored.dll）。

param(
    [string]$DeployDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "deploy")
)

$ErrorActionPreference = "Stop"

# ---- 关键文件清单（缺一即门禁转红）----
$required = @(
    "DataScope.exe",
    "simulator.exe",
    "Qt5Core.dll",
    "Qt5Gui.dll",
    "Qt5Widgets.dll",
    "Qt5Network.dll",
    "libgcc_s_seh-1.dll",
    "libstdc++-6.dll",
    "libwinpthread-1.dll",
    "platforms/qwindows.dll"
)

if (-not (Test-Path $DeployDir)) {
    Write-Error "部署目录不存在：$DeployDir（请先运行 scripts/release.ps1）"
    exit 1
}

$missing = @()
foreach ($f in $required) {
    if (-not (Test-Path (Join-Path $DeployDir $f))) {
        $missing += $f
    }
}

if ($missing.Count -gt 0) {
    Write-Error "部署不完整，缺少以下关键文件（共 $($missing.Count) 个）："
    $missing | ForEach-Object { Write-Error "  - $_" }
    exit 1
}

Write-Host "部署完整性校验通过：$($required.Count) 个关键文件齐全。"
exit 0
