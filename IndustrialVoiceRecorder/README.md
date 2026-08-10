# 工业语音记录系统

## 项目说明
这是一个基于WPF的工业现场语音数据采集系统，支持通过蓝牙耳机（如AirPods Pro）进行实时语音识别和记录。

## 技术栈
- .NET 8.0
- WPF + MVVM
- NAudio (音频采集)
- Whisper.cpp (离线语音识别)
- SQLite (数据存储)
- EPPlus (Excel导出)

## 项目结构
```
IndustrialVoiceRecorder/
├── src/
│   ├── IndustrialVoiceRecorder.sln
│   ├── IndustrialVoiceRecorder.Domain/           # 领域层
│   ├── IndustrialVoiceRecorder.Application/      # 应用层
│   ├── IndustrialVoiceRecorder.Infrastructure/   # 基础设施层
│   ├── IndustrialVoiceRecorder.WPF/             # 表现层
│   └── IndustrialVoiceRecorder.Shared/          # 共享库
├── tests/                                        # 测试项目
├── libs/                                         # 第三方库（Whisper.cpp）
└── docs/                                         # 文档
```

## 快速开始

### 1. 运行项目创建脚本
```bash
创建项目.bat
```

### 2. 构建项目
```bash
cd src
dotnet build
```

### 3. 运行应用
```bash
cd IndustrialVoiceRecorder.WPF
dotnet run
```

## 核心功能
- ✅ 蓝牙音频设备支持（AirPods Pro等）
- ✅ 实时语音识别（离线运行）
- ✅ 自动分类和统计
- ✅ SQLite本地存储
- ✅ Excel数据导出
- ✅ 历史记录查询

## 开发进度
- [x] 需求分析
- [x] 技术方案设计
- [ ] 项目结构创建（进行中）
- [ ] 核心功能开发
- [ ] 用户界面开发
- [ ] 测试与优化

## 文档
详细文档请查看 `docs/` 目录：
- 需求分析报告
- 解决方案策略
- 技术设计文档
- 数据库设计文档
- 用户使用手册

## 许可
MIT License
