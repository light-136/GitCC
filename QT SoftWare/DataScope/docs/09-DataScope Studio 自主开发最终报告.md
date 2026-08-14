# DataScope Studio 自主开发最终报告

> **项目全称**：DataScope Studio —— 工业多通道数据采集与实时监控系统
> **版本**：v0.1.0 ｜ **开发模式**：主 Agent + 专业 Agent 多角色自主开发
> **报告日期**：2026-08-14

---

## 一、项目概述

DataScope Studio 是一个**以真实工程系统性学习 Qt 的完整落地项目**。开发者从 WPF / C# 技术栈迁移，以"工业多通道数据采集与实时监控系统"为载体，通过 15 个渐进阶段（P1~P15）完成了从环境搭建、语言筑基、Qt 核心机制、分层架构，到 TCP 通信、多线程、状态机、自绘控件、记录回放、部署发布的全链路工程实践。

本项目不是教学 Demo，而是一个**可运行的完整工业级软件**：

- 支持 TCP 采集链路（模拟设备 + 真实协议解析）
- 实时监控界面（自绘曲线 / 仪表盘 / LED 指示灯）
- 数据记录与 CSV 历史回放
- 深色工业主题、日志系统、配置系统
- 12 个单元测试套件全绿、CTest 集成、发布包免安装即用

---

## 二、开发历程与阶段成果

| 阶段 | 主题 | 核心产出 | 验证方式 |
|------|------|----------|----------|
| P01 | 环境搭建与工程骨架 | CMake + Qt5 + Ninja + AUTOMOC 工程 | 空窗口可运行 |
| P02 | C++ 现代特性筑基 | 智能指针 / lambda / RAII 实践 | tst_byteutils |
| P03 | Qt 核心类型与隐式共享 | QString / QVariant / QVector | tst_timeutils |
| P04 | 元对象系统与 Signal/Slot | Q_OBJECT / connect 机制 | tst_signalslotdemo |
| P05 | UI 基础 + 主窗口 + QSS | MainWindow 骨架 + 深色主题 | 界面人工验收 |
| P06 | 日志系统 | LogManager（分级/落盘） | tst_logmanager |
| P07 | 配置系统 | ConfigManager（INI 持久化） | tst_configmanager |
| P08 | Domain 领域模型 | Device / Channel / DataPoint | tst_domain |
| P09 | 串口通信（预备） | 技术调研与接口预留 | — |
| P10 | 协议引擎 | Crc16 / FrameBuilder / FrameParser | tst_protocol |
| P11 | TCP + 模拟设备 | TcpClient + Simulator 双端 | tst_tcpclient |
| P12 | 多线程与异步 | AcquisitionWorker + DataService 数据总线 | tst_dataservice |
| P13 | 状态机与错误处理 | DeviceController + 自动重连退避 | tst_devicecontroller |
| P14 | 实时监控 + 自绘控件 | LineChartWidget / GaugeWidget / LedIndicator | tst_widgets |
| P15 | 记录回放 + 集成测试 + 部署 | RecordManager + RecordPage + 发布包 | tst_recordmanager + CTest 12/12 |

**开发原则**：每阶段严格遵循「编译 → 测试 → 文档 → 验收 → 进入下一阶段」的闭环，保证任意时刻工程处于**可运行、可测试、可交付**状态。

---

## 三、交付物清单

| 类别 | 交付物 | 位置 |
|------|--------|------|
| 源码 | 49 个源文件 / 约 4136 行 | `src/` |
| 测试 | 12 个测试套件 / 约 1769 行 | `tests/unittests/` |
| 模拟设备 | Simulator 独立可执行程序 | `simulator/`（构建产物在 build/） |
| 文档体系 | 项目规程 / 概述 / 总体设计 / 详细设计 / 构建部署 / 测试 / 使用说明 / 项目总结 / 最终报告 | `docs/` |
| 阶段文档 | P01~P15 阶段学习文档 | `docs/stages/` |
| 发布包 | DataScope.exe + simulator.exe + Qt 运行库（免安装） | `deploy/` |

---

## 四、架构与设计成果

### 4.1 分层架构

```
┌────────────────────────────────────────────────┐
│  UI 层（QWidget 页面 + 自绘控件）                │
│   MainWindow / MonitorPage / RecordPage / ...   │
├────────────────────────────────────────────────┤
│  app 层（程序入口 + SignalSlotDemo 教学对象）     │
├────────────────────────────────────────────────┤
│  services 层（业务服务）                         │
│   TcpClient / AcquisitionWorker / DataService   │
│   DeviceController / RecordManager              │
├────────────────────────────────────────────────┤
│  domain + protocol 层（领域模型 + 协议引擎）      │
│   models / Crc16 / FrameBuilder / FrameParser   │
├────────────────────────────────────────────────┤
│  infrastructure 层（基础设施）                   │
│   LogManager / ConfigManager                   │
├────────────────────────────────────────────────┤
│  utils 层（工具库）                              │
│   ByteUtils / TimeUtils                        │
└────────────────────────────────────────────────┘
```

采用 **5 个静态库**（datascope_utils / datascope_infra / datascope_domain / datascope_protocol / datascope_services）划分模块边界，主程序与测试共享同一批库——对应 WPF 中「类库项目 + 主项目 + 测试项目」的工程组织方式。

### 4.2 关键设计决策

1. **数据源可替换设计**：MonitorPage 只认 `onDataUpdated(QVector<DataPoint>)` 一个入口，P14 演示源 → P12 真实采集链路 → P15 回放数据共用同一接口，UI 层零改动。
2. **事件驱动代替同步阻塞**：TCP 通信采用信号槽事件驱动；回环测试发现 `waitForNewConnection` 存在信号竞态，改用 `QTRY_VERIFY_WITH_TIMEOUT` 事件驱动断言。
3. **DeviceController 状态机**：`Disconnected → Connecting → Connected / Error`，指数退避自动重连（1s→2s→4s→30s 封顶），并区分主动断开与意外断开。
4. **Worker 生命周期管理**：错误时必须销毁 TCP 客户端对象，否则 `start()` 的重复进入保护会吞掉重连。

### 4.3 协议设计

帧格式：`SOF(0xAA55, 2B) + FUNC(1B) + CMD(1B) + LEN(2B 大端) + DATA(N) + CRC16(2B MODBUS 低字节在前)`，最大负载 1024 字节，CRC 校验保证数据完整性。

---

## 五、测试与质量保障

### 5.1 测试结果：CTest 12/12 套件全绿

| 测试套件 | 被测模块 | 覆盖点 |
|----------|----------|--------|
| tst_byteutils | 字节工具 | 大小端转换 / 位操作 |
| tst_timeutils | 时间工具 | 时间戳格式化 |
| tst_signalslotdemo | 信号槽机制 | 信号触发 / 参数传递 |
| tst_logmanager | 日志系统 | 分级 / 落盘 / 线程安全 |
| tst_configmanager | 配置系统 | 读写 / 缺省值 |
| tst_domain | 领域模型 | 构造 / 复制 / 校验 |
| tst_protocol | 协议引擎 | CRC 校验 / 帧构建 / 半包粘包 |
| tst_widgets | 自绘控件 | 曲线数据 / 仪表范围 / LED 状态 |
| tst_tcpclient | TCP 客户端 | 连接 / 半包 / 粘包 / 连接失败 |
| tst_dataservice | 数据总线 | 多线程采集链路回环 |
| tst_devicecontroller | 状态机 | 状态转移 / 自动重连 / 数据转发 |
| tst_recordmanager | 记录回放 | CSV 落盘 / 加载 / 定时重放 |

### 5.2 关键测试难点与解决方案

- **`waitForNewConnection` 信号竞态**：同步阻塞 API 与 `QSignalSpy::wait()` 事件循环竞争消费信号 → 改为事件驱动 + `QTRY_VERIFY_WITH_TIMEOUT`。
- **Windows 端口拒绝延迟**：刚释放的 LISTEN 端口约 2s 后返回 ConnectionRefused，且相邻端口可能被系统服务占用导致误连接成功 → 用「listen 成功 = 此刻空闲」验证空闲端口，等待时间放宽到 6s。
- **enum class 元类型注册**：QSignalSpy 无法处理命名空间枚举 → `Q_DECLARE_METATYPE` + 注册裸名/相对名/全限定名三种别名。
- **跨线程信号参数注册**：`QVector<DataPoint>` 必须用全限定名并与 moc 规范化名一致。

### 5.3 端到端冒烟测试

`simulator + DataScope` 双进程真实链路验证：TCP ESTABLISHED、simulator 日志确认「新客户端连接」，完整走通 连接→采集→数据解析→UI 曲线 全链路。

---

## 六、工程化能力

- **构建**：CMake + Ninja，Debug/Release 双构建，AUTOMOC/AUTORCC 自动生成
- **部署**：`ds_deploy_qt()` 函数自动 windeployqt，一键部署 Qt 运行库到 exe 目录
- **测试**：CTest 注册 12 套件，一条命令全量回归
- **版本管理**：25 个语义化提交，每阶段独立提交，历史可追溯

---

## 七、Qt 学习成果总结（对照 WPF 迁移）

| WPF / C# | Qt / C++ | 本项目落点 |
|----------|----------|-----------|
| DependencyObject / 路由事件 | QObject + 信号槽 | P04 SignalSlotDemo |
| XAML / 绑定 | QWidget + QSS / 手动更新 | P05 深色主题 |
| INotifyPropertyChanged | 信号驱动的 UI 刷新 | P12 数据总线 |
| 后台 Task / async-await | QThread + moveToThread + 队列信号 | P12 AcquisitionWorker |
| 类库项目 | CMake 静态库 | 全工程模块划分 |
| WPF 自带图表 | QPainter 自绘 | P14 曲线/仪表/LED |
| StreamReader 写文件 | QFile / QTextStream | P15 CSV 记录 |
| Visual Studio 发布 | windeployqt 部署 | P15 发布包 |

**核心认知跃迁**：从「框架替你干活」到「理解引擎如何干活」——信号槽的直连/队列投递、隐式共享的写时复制、对象树生命周期管理，这些底层机制在本项目中都被真正用到并理解了。

---

## 八、多 Agent 协作模式总结

项目全程采用 **主 Agent（Project Lead）+ 专业 Agent 并行** 的开发模式：

- **角色分工**：架构决策、共享文件（CMakeLists）、阶段规划、测试设计由主 Agent 负责；UI 页面、文档撰写等可并行任务派发给专业 Agent。
- **并行规则**：接口已定义 → 并行；有依赖 → 串行。测试 Agent 独立于开发 Agent，防止「自己测自己」。
- **协作成果**：UI 页面（MonitorPage/DevicePage/RecordPage 等）、多份文档、阶段验证均有 Agent 并行产出，显著缩短工期。
- **集成纪律**：P15 前集成冻结，所有 Agent 产出先经编译与测试验证再合入。

---

## 九、发布包说明

发布包位于 `deploy/` 目录，**免安装、免 Qt 环境**，解压即用：

```
deploy/
├── DataScope.exe     # 主程序（监控界面）
├── simulator.exe     # 模拟采集设备（可选，无硬件时使用）
├── Qt5Core.dll       # Qt 运行库（windeployqt 部署）
├── Qt5Gui.dll / Qt5Widgets.dll / Qt5Network.dll ...
├── platforms/qwindows.dll   # Qt 平台插件（必需）
└── styles/ / iconengines/ / imageformats/  # 样式与图像插件
```

**使用三步**：
1. 双击运行 `simulator.exe`（模拟设备，监听 40001 端口）
2. 双击运行 `DataScope.exe`（自动连接模拟设备）
3. 在「监控总览」页观察实时曲线，在「数据记录」页记录与回放数据

详细操作见《07-使用说明.md》。

---

## 十、总结与展望

### 项目达成

- ✅ 15 个阶段全部完成，工程稳定可运行
- ✅ 12 个测试套件全绿，质量有保障
- ✅ 完整文档体系（规程/设计/测试/使用/总结/最终报告）
- ✅ 免安装发布包交付

### 未来方向

- P9 串口通信落地（接入真实硬件设备）
- 多设备同时采集与通道编排
- 波形缩放 / 光标测量等高级分析功能
- 数据分析（均值/方差/频谱）与报表导出

---

*本报告由主 Agent 基于真实源码、测试结果与 git 历史编写，所有数据可复核。*
