# 架构导览 / 源码行走（docs/learn）

> **目标**：Level 1「看懂代码」。读完本文，你能拿着本文件从 `main()` 一路走到曲线动起来，
> 途中看清每一层的文件、职责与数据流。建议对照源码边读边走。

---

## 1. 启动序列：`main()` 里发生了什么

入口：`src/app/main.cpp`（对应 WPF 的 App.xaml.cs）。

```cpp
QApplication app(argc, argv);                  // ① GUI 子系统 + 全局事件系统
LogManager::instance().init();                 // ② 日志先落地（后续任何模块写日志都有落点）
ConfigManager::instance().init();              // ③ 配置加载（INI，用户目录）
MainWindow window;                              // ④ 主窗口构造 = 层层组装 + 信号布线
window.show();
return app.exec();                              // ⑤ 进入事件循环：程序从此"活着"
```

**关键认知**：`exec()` 不是"忙等"，它**驱动事件循环**——所有信号槽、定时器、网络 IO 都在
`exec()` 期间被事件循环分发。`exec()` 返回 = 所有窗口关闭 = 程序结束。

---

## 2. 分层架构（CMake 静态库目标 = 物理分层）

| 层 | CMake 目标 | 目录 | 职责 | 依赖 |
|----|-----------|------|------|------|
| 工具层 | `datascope_utils` | `src/utils/` | 字节/时间工具 | 仅 QtCore |
| 基础设施 | `datascope_infra` | `src/infrastructure/` | 日志/配置（全局单例） | utils |
| 领域层 | `datascope_domain` | `src/domain/` | 纯数据模型（DataPoint/Device/报警事件…） | 仅 QtCore |
| 协议层 | `datascope_protocol` | `src/protocol/` | 帧组包/解析/CRC/通道配置编解码 | 仅 QtCore |
| 服务层 | `datascope_services` | `src/services/` | TCP/采集线程/数据总线/状态机/报警/记录 | domain+protocol |
| 模拟设备 | `datascope_simulator` | `src/simulator/` | 模拟采集设备（模式+异常注入） | protocol |
| 主程序 | `DataScope` | `src/ui/`+`src/app/` | 界面、控件、模型（Model/View） | 以上全部 |

**依赖方向**：`UI → services → (domain + protocol) → utils`；`simulator → protocol`。
**红线**：UI 不直接碰协议字节，services 不直接碰 UI 控件——一切通过信号槽/接口。

> 注：`src/controllers/`、`src/models/`、`src/widgets/`、`src/worker/` 是 V1 规划时的空目录，
> V2 已把实现收敛到 `src/ui/` 与 `src/services/`，这些空壳保留但不再新增内容。

---

## 3. 一条数据流贯穿全链路（本文件主线）

从「用户点连接」到「曲线动起来」，只走一条通路。逐跳看：

```
用户点击[连接设备]
   │  MainWindow::connectDevice()
   ▼
ConfigManager 读 host/port（默认 127.0.0.1:40001）
   │  m_service->connectTo(host, port)   ── 异步，立即返回
   ▼
DataService（主线程）
   │  invokeMethod(worker, "start", QueuedConnection)   ── 队列投递到采集线程
   ▼
AcquisitionWorker::start()（采集线程）
   │  ① new TcpClient（socket 在采集线程出生 ← 线程亲和铁律）
   │  ② 连接成功后 emit connectedChanged(true) → 主线程 onServiceConnected
   │  ③ 发送通道配置查询帧 {0x03, 0x01}
   ▼
SimulatorDevice（对端）
   │  收到查询 → 应答 0x83 配置帧（4 通道 名称/单位/量程）
   ▼
AcquisitionWorker::handleFrame()（采集线程）
   │  识别 0x83/0x01 → ChannelConfigCodec::decode
   │  emit channelConfigReceived(QVector<ChannelConfigInfo>)   ── 队列投递主线程
   ▼
DataService::channelConfigReceived ──→ MonitorPage::onChannelConfigReceived
   │  通道数变化 → rebuildChannelArea()；更新报警规则
   ▼
SimulatorDevice 每 50ms 发采集帧 {0x01, 0x01, 4×float 大端}
   ▼
AcquisitionWorker::handleFrame()（分流 2：采集帧）
   │  大端 float 解码 → QVector<DataPoint>
   │  emit pointsReady(points)   ── 队列投递主线程
   ▼
DataService::onPointsReady
   │  {锁} 更新快照 m_lastData → emit dataUpdated(points)
   ▼
MonitorPage::onDataUpdated  ──► 曲线 appendPoint / 仪表 setValue / LED 变色 / 报警评估
RecordManager::appendData   ──► 记录中则落盘 CSV
```

**这张图就是本项目的"脊柱"**。看懂每一跳的线程、信号、类型，就理解了 80% 的 DataScope。

---

## 4. 每一层的源码行走要点

### 4.1 工具层 `src/utils/`（P2/P3 基础）

- `ByteUtils`：字节 ↔ hex、大端读写——协议层的左膀右臂；
- `TimeUtils`：时间格式化/解析——数据打点、CSV 文件名的地基。

### 4.2 基础设施 `src/infrastructure/`（P6/P7）

- `LogManager`：**全局单例**（`instance()`），线程安全写盘 + 按天滚动 + `messageLogged` 信号；
  → UI 的 `LogPanel` 订阅该信号 = **观察者模式**（P6 + V2-执行③）；
- `ConfigManager`：INI 键值配置，类型安全读写。主窗口连接参数从这里读（去硬编码）。

### 4.3 领域层 `src/domain/`（P8 / V2-①）

- `models.h`：V1 基础模型 `DataPoint`（channelIndex/value/timestamp）；
- `domainmodel_v2.h`：V2 模型 `DeviceId/Device/ChannelConfig/DataPoint(质量)/AlarmRule/AlarmEvent/…`。
  领域层**只放数据不写逻辑**——逻辑在 services（报警评估）和 UI（展示）。

### 4.4 协议层 `src/protocol/`（P10 / V2-③）

- `Crc16`：MODBUS CRC16（反射表）；
- `FrameBuilder`：字段 → 完整帧字节（SOF/FUNC/CMD/LEN/DATA/CRC）；
- `FrameParser`：**三态状态机**流式解析（半包/粘包/错位重同步）——工业协议健壮性的核心；
- `ChannelConfigCodec`：通道配置 ⇄ 载荷字节（33B/通道定长）。
  → 全层**零网络依赖**，可纯单测（tst_protocol / tst_channelcodec）。

### 4.5 服务层 `src/services/`（P11-P13 / V2）

| 类 | 职责 | 关键教学点 |
|----|------|-----------|
| `TcpClient` | socket + 组帧/解析封装 | readyRead 只在**新数据到达**时发一次 |
| `AcquisitionWorker` | 采集线程工作对象 | moveToThread + 信号跨线程 |
| `DataService` | 数据总线（唯一数据通道） | 生产者-消费者 + 快照锁 |
| `DeviceController` | 状态机 + 退避自动重连 | 状态转移表（契约 07） |
| `AlarmEngine` | 报警规则评估 | 与 View 分离 → 可单测 |
| `RecordManager` | CSV 记录 + 回放 | QTimer 定时重放 |

### 4.6 模拟设备 `src/simulator/`（P11 / V2-③）

- `FaultInjector`：**纯逻辑**异常变换（正常帧 → 0..N 段异常字节）；
- `SimulatorDevice`：TCP 服务端 + 5 运行模式 + 配置应答；
- 独立程序 `simulator`：`-p/-m/-f/-i` 命令行可配置——**测试基础设施**。

### 4.7 UI 层 `src/ui/`（P5/P14/P15 / V2）

| 目录 | 内容 | 对应 WPF |
|------|------|---------|
| `ui/mainwindow.*` | 主窗口（菜单/工具栏/5 页签/状态栏/QSS） | MainWindow.xaml |
| `ui/pages/` | 监控/设备/记录/设置页 | 各 UserControl |
| `ui/widgets/` | 自绘曲线/仪表/LED/日志面板 | 自定义控件 |
| `ui/models/` | DeviceListModel / AlarmEventModel | ObservableCollection 的 Model 实现 |

---

## 5. 源码行走路线图（按阅读顺序）

```
1.  src/app/main.cpp                     → 程序如何出生
2.  src/ui/mainwindow.cpp 构造           → 页面组装 + 全局布线（数据流总图）
3.  src/ui/pages/monitorpage.cpp        → 消费端：数据如何变成曲线
4.  src/services/dataservice.cpp        → 总线：跨线程转发 + 快照
5.  src/services/acquisitionworker.cpp  → 生产端：socket → 帧 → DataPoint
6.  src/protocol/frameparser.cpp        → 字节 → 帧（状态机）
7.  src/simulator/simulatordevice.cpp   → 对端：帧从哪来、异常怎么注入
8.  反方向再看一遍 7→1                   → 每层的前置层此刻都"有感觉了"
```

**看完这条路线，试着不看源码画出第 3 节的数据流图**——能画出来，Level 1 达成。

---

## 6. 学习提示

- **别从细节入手**：先走数据流（本文件第 3 节），再钻具体类；
- **信号是公共函数**：`emit xxx()` 只是调用信号，触发所有已连接槽——搜 `emit` 就能找到"谁生产"；
- **跨线程看队列连接**：接收者在另一线程的信号，调用会被**打包投递**到对方事件循环——搜 `Q_DECLARE_METATYPE` 找跨线程参数类型；
- **对照契约文档**：协议细节在 `docs/contracts/03/05/06/07`，与源码注释互相印证。

---

*下一篇：`qt-concepts.md`（Qt 概念精讲）——为什么需要线程、为什么 moveToThread、为什么 QueuedConnection。*
