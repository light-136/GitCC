# DataScope Studio V2 工程质量提升计划

> **性质**：V1（P1~P15 Demo 集合）→ V2（真实中型工业监控软件）的完整重构计划
> **状态**：本文件为 V2 目标蓝图 + 执行路线（审查发现章节由严格审查报告支撑）
> **日期**：2026-08-15 ｜ **原则**：软件是第一目标，学习是第二目标；先有工程问题，再选 Qt 技术

---

## 一、V2 总目标

把"覆盖 Qt 知识点拼出的 Demo 集合"改造成 **"一个真正可以运行、值得开发、可以系统学习 Qt 的中型 Windows 工业监控软件"**。

### 核心转变

| V1（现状） | V2（目标） |
|-----------|-----------|
| 为了覆盖知识点塞控件 | 每个模块因工程需要而存在 |
| 几个 struct + 随机数字 | 真实领域模型 + 不变量 + 行为 |
| UI 直接显示数据 | View → Model → Domain 完整链路 |
| 模拟设备"让曲线动" | 模拟设备 = 测试基础设施（异常/压力注入） |
| 12 个单元测试"跑通" | Unit/Integration/Protocol/Thread/Error/Smoke/Deployment 分层体系 |
| 源码满屏教学注释 | 核心代码工程级质量，教学移入 docs/ |
| "A 对应 B"式 WPF 映射 | 代码层深入对比 + 为何不能一一对应 |
| 3 个真实功能页 + 2 个占位页 | 每个页面都有真实职责 |

---

## 二、V2 目标架构

```
┌────────────────────────────────────────────────────────────┐
│ UI 层（Qt Widgets + Model/View）                            │
│  MainWindow（应用框架）                                     │
│   ├─ 菜单栏 工具栏 设备导航(Dock) 主工作区 状态栏           │
│   ├─ 日志/报警 Dock（QDockWidget 可停靠）                   │
│   ├─ DeviceManagementView   设备管理（列表/状态/参数/连接）  │
│   ├─ MonitorView            实时监控（设备→通道→值/单位/报警）│
│   ├─ RecordView             记录/回放                        │
│   ├─ AlarmView              报警中心                         │
│   ├─ SettingsView           真实配置                         │
│   └─ 自绘控件（曲线/仪表/LED，V1 保留并提升）                │
├────────────────────────────────────────────────────────────┤
│ Controller 层（每视图一个控制器，瘦 UI）                     │
│  接收 UI 事件 → 调用应用服务 → 更新 Model → 刷新 View        │
├────────────────────────────────────────────────────────────┤
│ Application Service 层                                      │
│  ConnectionManager（连接/断线/重连策略）                    │
│  AcquisitionService（采集调度/通道管理）                    │
│  AlarmEngine（报警规则评估/事件产生）                       │
│  RecordService（记录/回放会话）                             │
│  DeviceManager（设备 CRUD/持久化）                          │
├────────────────────────────────────────────────────────────┤
│ Domain 层（真实领域模型 + 不变量 + 行为）                   │
│  Device / DeviceId / DeviceState                            │
│  Channel / ChannelConfig / DataPoint(时间序列)             │
│  AlarmRule / AlarmEvent / AcquisitionRecord                │
│  （值类型用 value class，聚合用 class，行为内聚）            │
├────────────────────────────────────────────────────────────┤
│ Model 层（Qt Model/View，本版本核心补课）                   │
│  DeviceListModel(QAbstractTableModel)                       │
│  ChannelTableModel(QAbstractTableModel)                     │
│  AlarmEventModel(QAbstractListModel)                        │
│  DataPointSeriesModel（供曲线控件消费）                     │
├────────────────────────────────────────────────────────────┤
│ Protocol 层（帧协议 + 编解码 + 校验，V1 保留并强化）         │
├────────────────────────────────────────────────────────────┤
│ Infrastructure 层（网络/存储/日志/配置，V1 保留并完善）      │
├────────────────────────────────────────────────────────────┤
│ Device/Simulator 层（设备抽象接口 + 可插拔实现）            │
│  DeviceInterface（抽象设备契约）                            │
│  SimulatorDevice（V2：正常/异常/压力模式 + 事件注入）        │
└────────────────────────────────────────────────────────────┘
```

**分层价值**：每一层都有存在的真实原因——UI 不碰协议、协议不碰线程、领域不碰 Qt 控件、设备可插拔替换。

---

## 三、V2 领域模型设计（核心补课）

### 3.1 类型清单

| 类型 | 性质 | 关键成员 | 不变量/行为 |
|------|------|----------|-------------|
| `DeviceId` | value class | uuid | 不可变 |
| `DeviceState` | enum class | Disconnected/Connecting/Online/Error/Configuring | 合法转移表 |
| `ConnectionParams` | value class | type(TCP/Serial)/host/port/baud/timeouts | 校验 |
| `ChannelConfig` | value class | name/unit/scale/offset/min/max/quality | 量程校验 |
| `Channel` | class | config + currentValue + timestamp + state | 更新校验 |
| `DataPoint` | value | timestamp/quality/raw/eng | 值语义 |
| `AlarmRule` | value | channelRef/condition/threshold/latch | 条件定义 |
| `AlarmEvent` | class | ruleRef/time/severity/message/ack | 状态机 |
| `AcquisitionRecord` | class | sessionId/start/end/channelMeta/采样率 | 会话完整性 |

### 3.2 关键点

- **Value vs Entity**：DataPoint/ChannelConfig 是值（可拷贝）；Device/Channel 是实体（有身份、有状态）。
- **不变量内聚**：量程、状态转移、报警确认等行为进领域类，不允许 UI 直接改裸成员。
- **领域零依赖**：domain 层不依赖 Qt Widgets/Network，只依赖 QtCore 容器（可被任意层复用）。

---

## 四、V2 UI 设计（专业监控软件标准）

### 4.1 主窗口框架

```
┌──────────────────────────────────────────────────────────┐
│ 菜单栏 文件|设备|采集|记录|视图|帮助                        │
├───────────────┬──────────────────────────────────────────┤
│ 工具栏         │                                          │
│ [连接][断开]   │    主工作区（QSplitter + QTabWidget）      │
│ [开始][停止]   │    ┌────────────────────────────────┐    │
├───────────────┤    │ 设备管理 | 实时监控 | 记录回放 | 报警 │
│ 设备导航 Dock  │    │                                │    │
│ ├─ 设备列表     │    │                                │    │
│ ├─ 设备状态     │    │                                │    │
│ └─ 通信参数     │    │                                │    │
├───────────────┼──────────────────────────────────────────┤
│ 日志/报警 Dock │    │  实时曲线 | 仪表 | 通道值表         │
├───────────────┴──────────────────────────────────────────┤
│ 状态栏 连接状态|采集状态|数据率|时间                       │
└──────────────────────────────────────────────────────────┘
```

### 4.2 设备管理页（V2 从占位变真功能）

- 设备列表：`QTableView + DeviceListModel`（名称/类型/状态/通道数/连接地址）
- 设备操作：新增/编辑/删除/复制/启停/测试连接
- 设备编辑对话框：名称/连接方式(TCP/串口)/地址/端口/波特率/通道配置(增删改通道名/单位/量程)
- 设备配置持久化（真实读写，可保存/加载/验证）

### 4.3 实时监控页（V2 专业信息层次）

每通道显示：**名称 | 实时值 | 单位 | 状态(正常/报警/超限) | 时间戳 | 迷你趋势 | 报警标记**
支持：多设备切换、通道筛选、报警高亮、暂停/恢复

### 4.4 报警中心

报警事件列表（QListView + AlarmEventModel）：时间/设备/通道/级别/描述/确认状态；报警确认/清除操作。

### 4.5 设置页（V2 从占位变真配置）

采样率/曲线窗口/日志级别/主题/数据存储路径 → 全部读写 ConfigManager。

---

## 五、V2 模拟设备（测试基础设施）

### 5.1 能力矩阵

| 能力 | V1 | V2 |
|------|----|----|
| 正常正弦数据 | ✅ | ✅ |
| 断线/恢复 | ❌ | ✅ 可定时/命令触发 |
| 延迟注入 | ❌ | ✅ 可配延迟 |
| 粘包/分包 | 仅协议层 | ✅ 可注入 |
| CRC 错误帧 | ❌ | ✅ 可注入 |
| 非法帧 | ❌ | ✅ 可注入 |
| 设备忙 | ❌ | ✅ 可注入（0x01 设备忙）|
| 报警触发/恢复 | ❌ | ✅ 值越限产生报警 |
| 数据突变 | ❌ | ✅ |
| 压力模式 | ❌ | ✅ 高频率/大数据量 |
| 模式切换 | ❌ | ✅ 正常/异常/压力 |

### 5.2 接口

- `SimulatorConfig`：通道数/采样率/帧间隔/异常注入列表
- 命令通道：`simulator --mode pressure --inject crc_error,disconnect` 或运行期控制
- 异常注入可脚本化 → 测试可在 CI 中复现

---

## 六、V2 测试体系

| 层 | 内容 | 工具 |
|----|------|------|
| Unit | 领域不变量、工具函数 | QtTest |
| Protocol | 组帧/解析/半包/粘包/CRC/非法帧（V1 已有，扩边界） | QtTest |
| Communication | 回环 TCP 连接/断线/重连/超时 | QtTest + 回环服务器 |
| Thread | Worker 启停/重复启停/退出时序/竞态 | QtTest + QTRY |
| Error | 错误注入后恢复、重连退避、报警产生 | QtTest + Simulator |
| Integration | DataService 端到端：设备→数据→Model→UI 信号 | QtTest |
| Simulator | 异常注入验证（注入 CRC 错→解析报错） | QtTest |
| Smoke | 进程级启动/连接/采集/记录/回放/退出 | 脚本 |
| Deployment | 发布包独立运行 | 脚本 + windeployqt |

**V2 必须覆盖的生命周期测试**：断线重连、程序退出、Worker 停止、Socket 销毁、多线程退出、连续大量数据、异常协议。

---

## 七、V2 教学体系（学习是第二目标，但必须成体系）

### 7.1 原则

- **核心源码 = 工程质量**（少量工程注释，无教学堆砌）
- **教学内容全部在 docs/learn/**，通过"学习映射表"关联到源码
- **知识点驱动从工程问题出发**：为什么需要线程→因为多设备采集不能阻塞 UI→为什么 moveToThread→因为要事件驱动 Worker→为什么 QueuedConnection→因为跨线程

### 7.2 学习路径 Level 1-7（目标）

| Level | 能力 | 载体 |
|-------|------|------|
| 1 | 看懂代码 | 架构导览 + 源码行走 |
| 2 | 修改功能 | 改通道数/量程/报警规则 |
| 3 | 独立增加模块 | 增加一种新设备类型 |
| 4 | 独立写 Qt 软件 | 小型完整应用 |
| 5 | 设计 Qt 架构 | 分层 + Model/View 设计 |
| 6 | 处理线程/网络/协议/生命周期 | V2 深度专题 |
| 7 | 读大型 Qt 项目 | 迁移到 Qt 开源项目实践 |

### 7.3 WPF→Qt 深入映射（docs/learn/wpf-qt-mapping.md）

每个概念：**WPF 机制 → Qt 对应 → 本质区别 → 为什么不能一一对应 → 本项目实际代码**。
重点：ObservableCollection→QAbstractItemModel、Binding→Model/View + dataChanged、Dispatcher→事件循环 + 队列信号、async/await→线程 + Worker + 队列连接、DependencyProperty→Q_PROPERTY。

---

## 八、V2 执行路线（依赖导向）

```
阶段A 领域模型 V2（无依赖，先行）────┐
阶段B 模拟设备 V2（独立）────────────┤
阶段C Model/View 层（依赖A）─────────┤→ 可并行
阶段D 服务层重构（依赖A/C）──────────┤
阶段E UI 重构（依赖C/D）─────────────┤
阶段F 测试体系补强（随各层）─────────┘
阶段G 教学/文档重构（依赖最终代码）
阶段H 集成验证 + 发布 + 最终报告
```

| 阶段 | 内容 | 验收 |
|------|------|------|
| A | 领域模型 V2 + 单测 | 模型单测全绿 |
| B | 模拟设备 V2（异常/压力注入）+ 验证 | 注入场景可复现 |
| C | DeviceListModel/ChannelTableModel/AlarmModel | Model 单测全绿 |
| D | DeviceManager/ConnectionManager/AlarmEngine/RecordService | 服务单测全绿 |
| E | 主窗口框架 + 设备管理/监控/报警/设置/记录页 | 人工验收 + 冒烟 |
| F | 全分层测试 + 生命周期测试 | CTest 分层全绿 |
| G | 教学体系 + WPF 映射 + 阶段文档 | 文档审核 |
| H | 集成验证 + Release 发布包 + 最终报告 | 全链路通过 |

---

## 九、评审与防回归

- **共享文件单写者**：CMakeLists 主 Agent 独占
- **每阶段门禁**：编译 + 该层测试全绿 + 集成不回归 → 才进下一阶段
- **V2 期间 git 提交按阶段**：可随时回退到稳定点
- **V1 保留**：V2 在 git 分支或独立目录推进，不动 V1 稳定交付（deploy/ 保留）

---

*本计划为 V2 目标蓝图。V1 具体问题清单见 `docs/reviews/01-DataScope V1 严格审查报告.md`（4 个专项 Agent 独立审查汇总）。*
