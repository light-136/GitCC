# -*- coding: utf-8 -*-
"""
FunASR 中文语音识别HTTP服务 (v2.1 配置化版)
- 从 config.txt 读取端口配置
- 如果找不到 config.txt，使用默认端口 10095
"""

import os
import json
import logging
import sys
import struct
import traceback
import numpy as np
from flask import Flask, request, jsonify

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[logging.StreamHandler(sys.stdout)]
)
logger = logging.getLogger(__name__)

app = Flask(__name__)
model = None

# 默认端口
DEFAULT_PORT = 10095


def read_config():
    """
    从 config.txt 读取配置
    查找顺序：
    1. 当前目录下的 config.txt
    2. 上一级目录的 src/.../config.txt
    3. 使用默认值
    """
    config = {}

    # 尝试多个路径查找 config.txt
    search_paths = [
        os.path.join(os.path.dirname(__file__), "config.txt"),
        os.path.join(os.path.dirname(__file__), "..", "src", "IndustrialVoiceRecorder.WPF", "config.txt"),
        os.path.join(os.path.dirname(__file__), "..", "src", "IndustrialVoiceRecorder.WPF", "bin", "Debug", "net8.0-windows", "config.txt"),
    ]

    config_path = None
    for path in search_paths:
        if os.path.exists(path):
            config_path = os.path.abspath(path)
            break

    if config_path:
        logger.info(f"读取配置文件: {config_path}")
        try:
            with open(config_path, 'r', encoding='utf-8') as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith('#'):
                        continue
                    if '=' in line:
                        key, value = line.split('=', 1)
                        config[key.strip()] = value.strip()
        except Exception as e:
            logger.warning(f"读取配置文件失败: {e}，使用默认配置")
    else:
        logger.info("未找到config.txt，使用默认配置")

    return config


def init_model():
    """初始化FunASR Paraformer中文模型"""
    global model
    logger.info("正在加载FunASR模型（首次运行需下载约1GB，请耐心等待）...")

    try:
        from funasr import AutoModel

        model = AutoModel(
            model="paraformer-zh",
            model_revision="v2.0.4",
            vad_model="fsmn-vad",
            punc_model="ct-punc",
            device="cpu",
        )

        logger.info("✅ FunASR模型加载完成！")
        return True

    except ImportError as e:
        logger.error(f"❌ FunASR未安装: {e}")
        logger.error("请运行: pip install funasr modelscope torch torchaudio flask -i https://mirrors.aliyun.com/pypi/simple/")
        return False
    except Exception as e:
        logger.error(f"❌ 模型加载失败: {e}")
        traceback.print_exc()
        return False


def pcm_to_float(pcm_data: bytes) -> np.ndarray:
    """将16位PCM字节数据转为归一化float32数组"""
    sample_count = len(pcm_data) // 2
    if sample_count == 0:
        return np.array([], dtype=np.float32)
    samples = struct.unpack(f'<{sample_count}h', pcm_data[:sample_count * 2])
    return np.array(samples, dtype=np.float32) / 32768.0


@app.route("/asr", methods=["POST"])
def asr():
    """语音识别接口（兼容 multipart/form-data 和 raw body 两种格式）"""
    global model

    if model is None:
        return jsonify({"text": "", "confidence": 0, "error": "模型未加载"}), 503

    try:
        # 兼容两种请求格式
        if request.files and "audio" in request.files:
            # multipart/form-data 格式（C# HttpClient发送的）
            audio_data = request.files["audio"].read()
        else:
            # raw body 格式
            audio_data = request.get_data()

        if not audio_data or len(audio_data) < 100:
            return jsonify({"text": "", "confidence": 0})

        audio_float = pcm_to_float(audio_data)
        duration = len(audio_float) / 16000

        logger.info(f"收到音频: {len(audio_data)}bytes, 时长={duration:.1f}秒")

        if duration < 0.3:
            return jsonify({"text": "", "confidence": 0})

        result = model.generate(input=audio_float, batch_size_s=300)
        logger.info(f"FunASR原始返回: {result}")

        if result and len(result) > 0:
            first = result[0]
            text = ""
            if isinstance(first, dict):
                text = first.get("text", "")
            elif isinstance(first, str):
                text = first
            elif hasattr(first, 'text'):
                text = first.text
            else:
                text = str(first)

            text = text.strip()
            logger.info(f"✅ 识别结果: \"{text}\"")
            return jsonify({"text": text, "confidence": 0.95})
        else:
            return jsonify({"text": "", "confidence": 0})

    except Exception as e:
        logger.error(f"识别出错: {e}")
        traceback.print_exc()
        return jsonify({"text": "", "confidence": 0, "error": str(e)})


@app.route("/health", methods=["GET"])
def health():
    """健康检查接口"""
    return jsonify({
        "status": "ok",
        "model_loaded": model is not None,
        "engine": "FunASR Paraformer-zh"
    })


if __name__ == "__main__":
    # 读取配置
    config = read_config()
    port = int(config.get("FunASR.Port", DEFAULT_PORT))
    host = config.get("FunASR.Host", "127.0.0.1")

    print("=" * 50)
    print("FunASR 中文语音识别服务 v2.1")
    print("=" * 50)

    if not init_model():
        print("模型初始化失败，退出")
        sys.exit(1)

    print("=" * 50)
    print(f"✅ 服务已启动！")
    print(f"   地址: http://{host}:{port}")
    print(f"   识别接口: POST http://{host}:{port}/asr")
    print(f"   健康检查: GET  http://{host}:{port}/health")
    print(f"   等待客户端连接...")
    print("=" * 50)

    # 关闭werkzeug多余日志
    wlog = logging.getLogger('werkzeug')
    wlog.setLevel(logging.WARNING)

    app.run(host=host, port=port, threaded=True)
