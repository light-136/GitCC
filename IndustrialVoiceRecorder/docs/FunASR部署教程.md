# FunASR 阿里达摩院语音识别 - 部署教程

## 一、FunASR简介

FunASR是阿里达摩院开源的语音识别工具包，核心模型 **Paraformer-zh** 专为中文优化：
- 中文识别准确率 **96%+**（远超Whisper的70%）
- 支持工业专业词汇（硅胶、丝印、NG等）
- 自带VAD语音检测 + 自动标点
- CPU即可运行，有GPU更快

---

## 二、安装步骤

### 2.1 安装Python
- 下载: https://www.python.org/downloads/
- 推荐版本: **Python 3.10**
- 安装时勾选 **"Add Python to PATH"**

验证：
```bash
python --version
pip --version
```

### 2.2 配置pip镜像源（加速下载）
```bash
pip config set global.index-url https://mirrors.aliyun.com/pypi/simple/
```

### 2.3 安装FunASR及依赖
```bash
pip install funasr modelscope torch torchaudio flask -i https://mirrors.aliyun.com/pypi/simple/
```

> 安装时间约5-15分钟，取决于网速。torch包约2GB。

### 2.4 验证安装
```bash
python -c "from funasr import AutoModel; print('FunASR安装成功')"
```

---

## 三、首次运行（模型下载）

首次运行FunASR时会自动下载以下模型：

| 模型 | 大小 | 用途 |
|------|------|------|
| paraformer-zh | ~800MB | 中文语音识别 |
| fsmn-vad | ~5MB | 语音活动检测 |
| ct-punc | ~200MB | 自动标点 |

模型缓存位置：
- Windows: `C:\Users\<用户名>\.cache\modelscope\`
- 可通过环境变量修改: `set MODELSCOPE_CACHE=D:\models`

> ⚠ 首次下载需要稳定网络，约10-20分钟

---

## 四、启动FunASR服务

### 方式1：使用批处理脚本
双击 `funasr/启动FunASR服务.bat`

### 方式2：命令行启动
```bash
cd funasr
python funasr_server.py
```

看到以下输出表示成功：
```
✅ FunASR模型加载完成！
✅ 服务已启动！
   地址: http://127.0.0.1:10095
   识别接口: POST http://127.0.0.1:10095/asr
   健康检查: GET  http://127.0.0.1:10095/health
```

### 测试识别
打开另一个CMD窗口：
```bash
curl http://127.0.0.1:10095/health
```
返回 `{"engine":"FunASR Paraformer-zh","model_loaded":true,"status":"ok"}` 表示正常。

---

## 五、config.txt配置

服务端口从 `config.txt` 读取：
```
FunASR.Host=127.0.0.1
FunASR.Port=10095
```

### 修改端口
如果10095被占用，修改 `config.txt` 中的 `FunASR.Port=新端口`，C#客户端和Python服务都会自动读取。

### 跨机器部署
如果FunASR运行在服务器A，WPF程序运行在客户端B：

**服务器A的config.txt：**
```
FunASR.Host=0.0.0.0    ← 监听所有网卡
FunASR.Port=10095
```

**客户端B的config.txt：**
```
FunASR.Host=192.168.1.100    ← 服务器A的IP地址
FunASR.Port=10095
```

---

## 六、GPU加速（可选）

如果有NVIDIA GPU，可大幅提升识别速度：

### 6.1 安装CUDA版PyTorch
```bash
pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu118
```

### 6.2 修改funasr_server.py
将 `device="cpu"` 改为 `device="cuda:0"`

---

## 七、性能参考

| 硬件 | 模型加载 | 识别速度(3秒音频) |
|------|---------|------------------|
| i5-8250U (CPU) | ~30秒 | ~2-3秒 |
| i7-12700H (CPU) | ~15秒 | ~1-2秒 |
| RTX 3060 (GPU) | ~10秒 | ~0.3秒 |

---

## 八、常见问题

### Q: pip install 报错 "Could not find a version"
使用阿里云镜像: `pip install ... -i https://mirrors.aliyun.com/pypi/simple/`

### Q: 模型下载超时
设置代理或手动下载模型到 `~/.cache/modelscope/` 目录

### Q: "No module named 'funasr'"
检查Python版本: `python --version`，确保 ≥ 3.8

### Q: 内存不足(OOM)
Paraformer模型约需2GB内存，确保系统有足够内存

### Q: 端口被占用
修改 `config.txt` 中的 `FunASR.Port` 为其他端口（如10096）
