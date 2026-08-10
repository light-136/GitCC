# -*- coding: utf-8 -*-
import os
# ǿ��ģ�����ص�D�̣����ȼ����
os.environ["MODELSCOPE_HUB"] = r"D:\Gemi\IndustrialVoiceRecorder\funasr\model_cache"

from funasr import AutoModel
import json
from flask import Flask, request

app = Flask(__name__)
# ��ҵ���������ĸ߾���ģ��+����VAD
model = AutoModel(model="paraformer-zh", model_revision="v2.0.4", vad_model="fsmn-vad")

@app.route("/asr", methods=["POST"])
def asr():
    audio_bin = request.files["audio"].read()
    result = model.generate(input=audio_bin)
    return json.dumps(result[0]["text"], ensure_ascii=False)

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=10095, debug=False)