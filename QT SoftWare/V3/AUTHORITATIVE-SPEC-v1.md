# V3 权威技术规格（架构师产出，v1）

> **本文件是 DataScope Studio V3 的唯一权威技术基准。**
>
> - 来源：对话中我以文字呈现的「第一部分｜V3 技术规格（架构师产出，v1）」（2026-08-15），用户明确指定以此为准。
> - `docs/` 目录下的其他文件是**更早的草案**，部分内容未同步 v1 修订；**凡与本文冲突，一律以本文为准**。
> - 本文 = 15 项规格 + 独立 Reviewer 6 条反驳 + 修订结论，全文照录，作为编码与后续评审的基准。

---

# 第一部分｜V3 技术规格（架构师产出，v1）

## 1. 项目目录结构（库 + 2 可执行，无空层）

```
V3/
├── CMakeLists.txt
├── src/
│   ├── app/main.cpp                 # 入口 + Composition Root（组装 worker/ui/infra）
│   ├── domain/                      # 纯规则，仅 QtCore
│   │   ├── datapoint.h              # DataPoint 值对象
│   │   ├── channelconfig.h          # ChannelConfig 值对象
│   │   ├── alarmrule.h              # AlarmRule / AlarmEvent
│   │   ├── alarmengine.h/.cpp       # 报警评估状态机（纯函数，无 Qt 信号）
│   │   ├── connectionstate.h        # ConnectionState + 退避时间纯函数
│   │   └── errorcode.h              # ErrorCode 枚举
│   ├── protocol/                    # 纯字节，仅 QtCore，不依赖 domain
│   │   ├── frametypes.h             # 帧字段/常量（含版本号/长度上限）
│   │   ├── crc16.h/.cpp
│   │   ├── framebuilder.h/.cpp
│   │   ├── frameparser.h/.cpp       # 读游标去 O(n)
│   │   └── channelconfigcodec.h/.cpp
│   ├── acquisition/                 # I/O + 跨线程编排
│   │   └── acquisitionworker.h/.cpp # Worker：moveToThread + 编排 + 转发（合并 TcpClient）
│   ├── ui/                          # 渲染 + 交互编排
│   │   ├── mainwindow.h/.cpp
│   │   ├── monitorpage.h/.cpp       # 纯渲染，信号直连
│   │   ├── models/alarmeventmodel.h/.cpp
│   │   ├── widgets/  linechartwidget / gaugewidget / ledindicator
│   │   └── theme.h/.cpp             # 颜色/字体 token（单一来源）
│   └── infra/                       # 支撑设施（具体类，非抽象）
│       ├── config.h/.cpp            # QSettings 读 host/port
│       └── logger.h/.cpp            # 分级 + 写文件（极简）
├── simulator/                       # 独立测试设备程序
│   ├── main.cpp
│   ├── simulatordevice.h/.cpp       # QTcpServer 周期广播
│   └── faultinjector.h/.cpp         # 纯函数异常注入
└── tests/
    ├── unit/                        # gtest：protocol + domain
    └── integration/                 # QtTest：simulator + worker 全链路
```

> 没有 `services/` 层、没有 `utils/` 层、没有 `IDevicePort/IClock/IDataSource` 接口——这些都删了或推迟了。

## 2. 模块职责（每模块一句话）

| 模块 | 一句话职责 | 禁止 |
|------|-----------|------|
| `protocol` | 帧的编解码与 CRC，只认字节 | include domain |
| `domain` | 值对象 + 报警规则 + 连接状态转移 + 错误码，纯规则无 I/O | include Widgets/Network/文件系统 |
| `acquisition` | 用 socket 收字节 → 喂 parser → 转发 DataPoint，管理采集线程 | 做业务判断/报警 |
| `ui` | 渲染 + 收集输入 + 交互编排 | 做协议解析/报警规则 |
| `infra` | config 读配置 + logger 写日志（具体类） | 做业务/协议 |
| `simulator` | 可复现异常注入的模拟设备（独立进程） | 依赖 ui |

## 3. 模块依赖图（依赖方向向下，domain 与 protocol 平行）

```
        app（Composition Root，唯一知道具体类的地方）
        │
        ├──▶ ui ────────────┐
        │    │              │
        │    ├──▶ acquisition
        │    │      ├──▶ domain
        │    │      ├──▶ protocol
        │    │      └──▶ infra(config/logger)
        │    │
        │    ├──▶ domain
        │    ├──▶ protocol
        │    └──▶ infra
        │
        └──▶ simulator 独立程序：只依赖 protocol + QtNetwork
```

**红线**：`domain` 只 include QtCore；`protocol` 只 include QtCore 且不 include domain。这两条用 CMake target 白名单 + 头文件扫描**机械验证**。

## 4. Domain 数据模型（纯值对象 + 纯函数）

```cpp
// 值对象：纯 struct，可拷贝可比较，不继承 QObject
struct DataPoint     { int channelIndex; double value; QDateTime deviceTs; QDateTime hostArrivalTs; };
struct ChannelConfig { QString name; QString unit; double rangeMin; double rangeMax; };
struct AlarmRule     { int channelIndex; double threshold; double hysteresis; };
struct AlarmEvent    { enum Type {Trigger, Recover}; Type type; int channelIndex; QDateTime ts; };

// 纯函数（规则，无副作用、无 I/O、无 Qt 信号）
QVector<AlarmEvent> AlarmEngine::evaluate(const DataPoint&);  // 有内部状态（哪些通道在报警中），输入输出都是值对象
bool  ConnectionState::canTransition(from, to);
int   nextRetryDelay(attemptCount);   // 退避：1s→2s→4s→封顶 30s

// 错误码：分类枚举，第一阶段不加"致命性维度"（那是后续演进）
enum class ErrorCode { Ok, ConnectTimeout, ConnectionRefused, CrcError, ParseError, ReceiveTimeout, InvalidFrame };
```

> **关键**：`AlarmEngine` 放 domain 因为它本质是业务规则（无 I/O 无线程可纯测）。它不做"怎么通知 UI"——那是 ui 的活。

## 5. TCP/协议数据流

```
Simulator ──TCP──▶ AcquisitionWorker(采集线程) ──readyRead(QByteArray)──▶ FrameParser
                                                                     │ feed/next
                                                          ┌──────────┴──────────┐
                                                     合法帧(带version)      错误事件(CrcError/InvalidFrame)
                                                          │                      │
                                                   帧→DataPoint映射           丢弃+计数+发 errorOccurred
                                                          │
                                                    emit pointsReady(QVector<DataPoint>)
                                                          │  QueuedConnection 跨线程
                                                          ▼
                                                    主线程 MonitorPage
```

帧格式（V2 6 字节头基础上**加 1 字节 version**，兼容旧帧）：`SOF(0xAA55) | VERSION(1B) | FUNC | CMD | LEN(2B 大端) | DATA | CRC16`。version 字节让新旧帧可识别共存。

## 6. 线程模型（1 采集线程，不是"固定 N"）

| 线程 | 数量 | 职责 |
|------|------|------|
| 主线程（GUI） | 1 | UI 渲染 + Composition Root |
| 采集线程 | 1 | 持有 AcquisitionWorker + FrameParser，收字节→解析→转发 |

- **没有队列**：`emit pointsReady(points)` 连 3 个 slot（渲染/报警/可选记录）就是天然多播，80 点/s 下 QVector 隐式共享浅拷贝零压力。
- **没有分片器、没有"每设备一线程"**：单设备 1 线程；多设备阶段再长分片器。
- 跨线程只传值对象（`QVector<DataPoint>`），元类型注册集中在 `metatype_registry.cpp` 一处。

## 7. QObject 生命周期模型

| 对象 | 归属线程 | 关停 |
|------|---------|------|
| `AcquisitionWorker`（QObject） | 采集线程 | 主线程 `stop()` → 等 Worker 回 `stopped()` 确认 → `quit()+wait()` |
| UI 对象 | 主线程 | 对象树回收；关闭事件先 `stopAcquisition()` |

**关停协议**（五件套）：握手确认 → socket `abort()` → 超时兜底 QTimer → 关停期抑制 connected/disconnected 信号 → `wait()` 超时不析构共享资源。

## 8. 错误处理策略

- **边界返回**（connect/parse/load）：返回 `ErrorCode`/`std::optional`，标注 `[[nodiscard]]`。
- **纯函数**（crc16/evaluate/退避）：裸值，不包装。
- **不静默吞**：CRC 错 → 丢弃+计数；非法帧 → 丢弃+告警（不立即断开）；连接超时/接收超时 → 触发重连。
- **跨线程**：worker 发 `connectionError(ErrorCode)` → UI 显示。**第一阶段不引入"致命性维度"枚举**（那是后续"分类降级"演进）。

## 9. Simulator / FaultInjector 设计

- `SimulatorDevice`：QTcpServer，监听端口，周期广播帧；5 运行模式 + 命令行 `-p/-m/-f/-i`（沿用 V2 资产）。
- `FaultInjector`：**纯函数**，`正常帧 → 0..N 段异常字节流`（粘包/分包/坏CRC/非法帧/突变），无状态、可复现。
- 二者是**测试基础设施**，与生产代码同等质量；simulator 是独立进程，也是 Integration/E2E 的依赖。

## 10. UI 数据流（按频率分流）

| 数据 | 频率 | 载体 |
|------|------|------|
| 实时值（曲线/仪表/LED） | 10~20Hz × N 通道 | **直绘控件 `setValue`**，不走 dataChanged |
| 报警事件 | 事件级 | `AlarmEventModel`（QAbstractListModel + beginInsertRows） |
| 通道配置 | 连接建立时一次 | `onChannelConfigReceived` → 重建卡片 |

- 阈值**一次性注入**：配置变化回调里 `chart->setThresholdLine(rule.threshold)`，非每帧查询。
- MonitorPage 纯渲染直连；连接入口放主窗口工具栏（不建 DevicePage）。
- 颜色走 `Theme` 单一来源，禁裸 hex。

## 11. 测试策略（第一阶段两层，不做四层）

| 层 | 框架 | 测什么 | 边界判据 |
|----|------|--------|---------|
| Unit | gtest | protocol（crc/半包粘包/重同步/越界）+ domain（报警触发去重恢复/状态转移/退避） | 只链接 domain+protocol，禁 QtNetwork |
| Integration | QtTest | simulator + worker + 主线程全链路（真实 socket 粘包重组/CRC 丢弃/配置查询） | 真实 simulator，禁 mock socket |

- **门禁**：`build_and_verify.ps1` 单命令，configure+build+ctest，任一非零即整体非零。
- **变异冒烟**：删关键逻辑 → 测试必须转红。
- 时序测试诚实划分：纯函数用 fake clock 确定性测；socket 断连用 `QTRY_VERIFY_WITH_TIMEOUT`（探针，非证明）。

## 12. Release / Deploy 策略

- **单一版本源**：`project(VERSION)` → `configure_file` 生成 `version.h` + `version.rc`（PE 资源），只改一处。
- **windeployqt 配置期硬失败**：`find_program(... REQUIRED NO_DEFAULT_PATH)`，找不到 `FATAL_ERROR`。
- **deploy 不入 git**：`.gitignore` 覆盖，发布包是带版本号 zip。
- 工具链版本硬校验（Qt 5.12.2 + MinGW 7.3.0 posix-seh）。

## 13. V2 迁移 / 参考 / 废弃

- **迁移**（改写成 V3 代码）：FrameParser 思路、Crc16、FrameBuilder、ChannelConfigCodec、FaultInjector、SimulatorDevice、自绘三控件。
- **参考**（不迁移）：旧帧格式契约（作版本协商兼容基线）、V1 审查报告（失败样本）、学习体系。
- **废弃**（不迁移不参考）：`DeviceController` 死代码、V1/V2 双领域模型、`kAlarmRatio=0.85`、`SignalSlotDemo/DemoPage/onDemoTick`、LogManager/ConfigManager 单例、windeployqt 静默跳过、deploy 里的 DLL、5 处硬编码版本号、元类型双注册。

## 14. 第一阶段明确"不做什么"

不记录/回放、不串口、不多设备、不数据分析、不 JSON 场景文件、不 benchmark、不 CI 服务器、**不空接口/空 target/空抽象**、不以代码/测试/文档/commit 数量为 KPI、`AlarmEngine` 不加致命性维度、测试不做四层（只两层）。

## 15. 后续需求触发架构自然演进

| 触发需求 | 长出的抽象 |
|---------|-----------|
| 单设备 → 多设备 | IDevicePort 接口 + 分片器 |
| 实时 → 实时+回放 | Source 状态机 |
| TCP → 串口(P9) | ITransport/IFraming 分层 |
| >1000 批/s 或渲染慢 | 有界队列 + 扇出 + 丢帧降级 |
| 记录/回放 | RecordManager + CSV |
| 错误分类降级 | ErrorCode 致命性维度 |

---

# 第二部分｜独立 Reviewer 反驳（6 个真问题）

**R1（P1）`AlarmEngine` 放 domain 但"有状态"，是否破坏"domain=纯规则"？**
它有内部状态（哪些通道在报警中），但它不依赖 Qt、无 I/O、输入输出全是值对象，本质仍是纯规则。**接受**，但必须约束：AlarmEngine 不继承 QObject、不发信号、不碰 QDateTime 之外的任何东西——它的状态是"评估上下文"，不是"对象生命周期"。这条要写死。

**R2（P1）`TcpClient` 和 `AcquisitionWorker` 两个 QObject 是否又是 V2 的"分层遗留"？**
V2 的错误就是"错误时要销毁 TcpClient 对象"导致生命周期复杂。单设备单线程下，两者分离的价值只剩"tcpclient 可单独 component 测"。**反驳结论**：第一阶段合并为一个 `AcquisitionWorker`（内部持有 QTcpSocket），减少一个类的生命周期管理。**采纳：合并，砍掉独立 TcpClient**。

**R3（P1）`DevicePage + Presenter` 第一阶段是否过度？**
第一阶段设备管理页就一个 host/port 输入 + 连接按钮，交互编排极薄。Presenter 会退化成"转发器"（V2 门面病翻版）。**反驳结论**：第一阶段不建 DevicePage 独立页 + Presenter，连接入口放主窗口工具栏，参数读 infra::config。**采纳：第一阶段砍 DevicePage**。

**R4（P1）`ChannelConfigCodec`（通道配置查询链路）第一阶段是否必要？**
它正确（设备是唯一事实来源），但增加 protocol+worker+ui 三层复杂度。**反驳结论**：保留，因为它是"数据驱动渲染"这个核心价值的载体，且资产成熟（V2 已验证）。**有条件保留：先做数据驱动，因为这是工业软件的正确性，不是魔法数**。

**R5（P2）`infra` 库（config+logger）是否也是"为了分层而分层"？**
config 就 2 个键（host/port），logger 用 qDebug 也够。独立成库 = 过度？**反驳结论**：config 和 logger 是真实被多处使用的**具体类**（不是抽象），放一个薄 infra 库不算空层。但必须约束 <200 行，且禁止做成单例（V2 教训）。**有条件保留：极薄，非单例，构造注入**。

**R6（P2）错误处理砍掉"致命性维度"后，重连策略会不会退化成 V2 的"一错就断"？**
第一阶段用简单 if-else：CRC 错丢弃计数、非法帧丢弃告警、超时重连。这**不**等于"一错就断"（断开的触发是"连接超时/接收超时"，不是"CRC 错"）。致命性维度是"连续 N 错 / 持续 T 秒才断开"的精细化，第一阶段不需要。**接受：砍致命性维度，但断开触发明确为"超时类错误"，单次 CRC/非法帧不断开**。

---

# 第三部分｜修订结论（架构师采纳 Reviewer 后）

采纳 R2、R3、R6，有条件采纳 R4、R5，接受 R1：

1. **合并 TcpClient 进 AcquisitionWorker**——砍一个 QObject，生命周期简化（1 个采集线程对象）。
2. **砍 DevicePage + Presenter**——第一阶段连接入口放主窗口工具栏，参数读 config。
3. **错误策略明确**：断开触发 = 超时类错误；单次 CRC/非法帧只计数告警不断开。
4. **保留**：ChannelConfigCodec（数据驱动）、infra config/logger（极薄非单例）、AlarmEngine 在 domain（约束为纯规则）。

**修订后 B 版体量估算**：约 3000 行核心代码（protocol ~550 / domain ~500 / acquisition ~250 / ui+自绘 ~1100 / infra ~150 / simulator ~400 / tests ~700）。

---

# 执行红线（供编码阶段机械遵守）

- 命名空间统一 `dscope`（`dscope::domain`、`dscope::protocol`、`dscope::acquisition`、`dscope::ui`、`dscope::infra`）；CMake target 名 `dscope_domain` / `dscope_protocol` / `dscope_acquisition` / `dscope_ui` / `dscope_infra`。
- `domain` 与 `protocol` 只 link `Qt5::Core`，白名单机械验证。
- `FrameParser` 用读游标（`m_readPos`），禁 `remove(0,n)` 消费。
- 帧头 `kHeaderLen=7`（SOF 2B + VERSION 1B + FUNC 1B + CMD 1B + LEN 2B），CRC16 覆盖从 VERSION 起。
- 断开只由「超时类错误」触发；单次 CRC/非法帧只计数告警。
- 每个阶段：编译 → 测试 → 修复 → 重编译 → 重测试，绝不最后一次性编译。
