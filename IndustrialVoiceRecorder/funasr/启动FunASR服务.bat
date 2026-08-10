@echo off
chcp 65001 >nul
cd /d D:\Gemi\IndustrialVoiceRecorder\funasr
REM 全局指定模型缓存目录
set MODELSCOPE_HUB=D:\Gemi\IndustrialVoiceRecorder\funasr\model_cache
if not exist "%MODELSCOPE_HUB%" mkdir "%MODELSCOPE_HUB%"
echo 模型缓存目录：%MODELSCOPE_HUB%
echo 正在启动离线语音识别服务...
echo.
py asr_server.py
pause