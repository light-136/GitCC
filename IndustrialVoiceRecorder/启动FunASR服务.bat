@echo off
chcp 65001 >nul
echo ========================================
echo FunASR 中文语音识别服务
echo ========================================
echo.
echo 端口配置从 config.txt 读取
echo 按 Ctrl+C 停止服务
echo ========================================
echo.

python "%~dp0funasr_server.py"

if errorlevel 1 (
    echo.
    echo ========================================
    echo 启动失败！请检查：
    echo 1. Python 是否已安装？ (python --version)
    echo 2. FunASR 是否已安装？ (pip install funasr modelscope torch torchaudio flask)
    echo 3. 使用阿里云镜像加速：pip install ... -i https://mirrors.aliyun.com/pypi/simple/
    echo ========================================
)

pause
