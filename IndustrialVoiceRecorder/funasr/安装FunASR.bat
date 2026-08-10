@echo off
chcp 65001 >nul
echo ========================================
echo FunASR 中文语音识别引擎 - 安装脚本
echo ========================================
echo.

REM ========== 新增：指定模型缓存到D盘项目目录 ==========
set MODELSCOPE_HUB=D:\Gemi\IndustrialVoiceRecorder\funasr\model_cache
echo [提示] 语音模型将下载至：%MODELSCOPE_HUB%
if not exist "%MODELSCOPE_HUB%" mkdir "%MODELSCOPE_HUB%"
echo.

REM 检查Python（全部替换为py）
py --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到Python，请先安装Python 3.8+
    echo 下载地址: https://www.python.org/downloads/
    echo 安装时请勾选 "Add Python to PATH"
    pause
    exit /b 1
)

echo [1/3] 检测到Python:
py --version

echo.
echo [2/3] 安装FunASR及依赖（首次需要几分钟）...
py -m pip install funasr modelscope torch torchaudio flask -i https://mirrors.aliyun.com/pypi/simple/

echo.
echo [3/3] 安装websockets...
py -m pip install websockets -i https://mirrors.aliyun.com/pypi/simple/

echo.
echo ========================================
echo 安装完成！
echo.
echo 使用方法:
echo   1. 运行 "启动FunASR服务.bat" 启动识别服务
echo   2. 启动 工业语音记录系统 WPF程序
echo   3. 程序会自动连接FunASR服务
echo ========================================
pause