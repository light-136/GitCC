# config.txt 配置说明

## 文件位置
程序根目录下的 `config.txt`，修改后需**重启程序**生效。

## 格式说明
- 每行一个配置项，格式: `Key=Value`
- `#` 开头为注释行
- 空行自动忽略
- 配置文件不存在时使用默认值，程序不会崩溃

---

## 配置项详解

### FunASR 语音识别服务

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `FunASR.Host` | 127.0.0.1 | FunASR服务的IP地址 |
| `FunASR.Port` | 10095 | FunASR服务的端口 |
| `FunASR.Endpoint` | /asr | 识别接口路径 |
| `FunASR.TimeoutSeconds` | 30 | HTTP请求超时时间（秒） |

**场景示例：**

同一台电脑部署（默认）:
```
FunASR.Host=127.0.0.1
FunASR.Port=10095
```

FunASR在局域网另一台服务器:
```
FunASR.Host=192.168.1.100
FunASR.Port=10095
```

---

### 数据库

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `Database.FileName` | voice_records.db | SQLite数据库文件名 |
| `Database.Directory` | Data | 数据库目录（相对或绝对路径） |

**示例：** 数据库放在D盘:
```
Database.Directory=D:\VoiceData
```

---

### 目录配置

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `Export.Directory` | Export | Excel导出文件目录 |
| `Log.Directory` | Logs | 日志文件目录 |
| `Model.Directory` | Models | Whisper模型目录 |

---

### VAD 语音检测参数

这些参数控制系统如何判断"用户在说话"，**对识别效果影响最大**。

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `VAD.SilenceTimeoutMs` | 2000 | 静音超时（毫秒）。说完话后等多久触发识别 |
| `VAD.MinAudioBytes` | 48000 | 最小音频长度（字节）。低于此值不发送识别 |
| `VAD.MaxBufferBytes` | 480000 | 最大缓冲（字节）。超过强制识别，防内存溢出 |
| `VAD.PreBufferBytes` | 16000 | 预缓冲（字节）。保留语音开头不丢失 |
| `VAD.EnergyMultiplier` | 2.5 | 能量倍数。噪音基线 × 此值 = 语音判定阈值 |
| `VAD.MinThreshold` | 0.0003 | 最小绝对阈值。防止极安静环境误触发 |

**换算关系：**
- 48000 bytes = 1.5秒音频（16kHz × 2字节 × 1.5秒）
- 480000 bytes = 15秒音频
- 16000 bytes = 0.5秒音频

**场景调优建议：**

| 场景 | SilenceTimeoutMs | MinAudioBytes | EnergyMultiplier |
|------|-----------------|---------------|-----------------|
| 安静办公室 | 2000 | 48000 | 2.5 |
| 嘈杂工厂 | 2500 | 64000 | 4.0 |
| 有线麦克风 | 1500 | 32000 | 2.0 |
| 蓝牙耳机 | 2000 | 48000 | 2.5 |
| 短命令（"OK""NG"） | 1500 | 24000 | 2.0 |
| 长报告 | 3000 | 48000 | 3.0 |

**调优原则：**
- 经常漏掉开头 → 增大 `PreBufferBytes`
- 说话中间被截断 → 增大 `SilenceTimeoutMs`
- 短词识别不到 → 减小 `MinAudioBytes`
- 环境噪音触发误识别 → 增大 `EnergyMultiplier`
- 轻声说话检测不到 → 减小 `MinThreshold`

---

### 应用信息

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `App.Name` | 工业语音记录系统 | 应用名称 |
| `App.Version` | 2.1.0 | 版本号 |
