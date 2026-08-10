@echo off
chcp 65001 >nul
echo ========================================
echo 工业语音记录系统 - 项目创建脚本
echo ========================================
echo.

REM 设置项目根路径
set ROOT_PATH=D:\Gemi\IndustrialVoiceRecorder

echo [1/5] 创建项目根目录...
if not exist "%ROOT_PATH%" mkdir "%ROOT_PATH%"
cd /d "%ROOT_PATH%"

if not exist "src" mkdir "src"
cd src

echo [2/5] 创建解决方案...
dotnet new sln -n IndustrialVoiceRecorder

echo [3/5] 创建各层项目...
echo   - 创建 Domain 层...
dotnet new classlib -n IndustrialVoiceRecorder.Domain -f net8.0
dotnet sln add IndustrialVoiceRecorder.Domain\IndustrialVoiceRecorder.Domain.csproj

echo   - 创建 Application 层...
dotnet new classlib -n IndustrialVoiceRecorder.Application -f net8.0
dotnet sln add IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj

echo   - 创建 Infrastructure 层...
dotnet new classlib -n IndustrialVoiceRecorder.Infrastructure -f net8.0
dotnet sln add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj

echo   - 创建 WPF 项目...
dotnet new wpf -n IndustrialVoiceRecorder.WPF -f net8.0
dotnet sln add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj

echo   - 创建 Shared 层...
dotnet new classlib -n IndustrialVoiceRecorder.Shared -f net8.0
dotnet sln add IndustrialVoiceRecorder.Shared\IndustrialVoiceRecorder.Shared.csproj

echo [4/5] 配置项目引用关系...
dotnet add IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj reference IndustrialVoiceRecorder.Domain\IndustrialVoiceRecorder.Domain.csproj
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj reference IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj reference IndustrialVoiceRecorder.Domain\IndustrialVoiceRecorder.Domain.csproj
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj reference IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj reference IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj reference IndustrialVoiceRecorder.Shared\IndustrialVoiceRecorder.Shared.csproj

echo [5/5] 安装 NuGet 包...
echo   - Application 层...
dotnet add IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add IndustrialVoiceRecorder.Application\IndustrialVoiceRecorder.Application.csproj package Microsoft.Extensions.Logging.Abstractions

echo   - Infrastructure 层...
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package NAudio
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package Microsoft.Data.Sqlite
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package Dapper
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package EPPlus
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package Serilog
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package Serilog.Sinks.File
dotnet add IndustrialVoiceRecorder.Infrastructure\IndustrialVoiceRecorder.Infrastructure.csproj package Serilog.Sinks.Console

echo   - WPF 层...
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj package CommunityToolkit.Mvvm
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj package Microsoft.Extensions.DependencyInjection
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj package Microsoft.Extensions.Hosting
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj package MaterialDesignThemes
dotnet add IndustrialVoiceRecorder.WPF\IndustrialVoiceRecorder.WPF.csproj package Serilog.Extensions.Hosting

echo.
echo ========================================
echo 项目创建完成！
echo 项目路径: %ROOT_PATH%
echo ========================================
echo.
pause
