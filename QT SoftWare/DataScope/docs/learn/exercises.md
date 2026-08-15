# Level 1-7 阶段练习（docs/learn）

> **用法**：每级 = 能力 → 载体 → 练习任务 → 验收标准 → 答案索引。
> 先不看答案尝试；卡住再看索引指向的源码/文档。完成"验收标准"才算过关。
> 全部练习都在 `D:\Gemi\QT SoftWare\DataScope` 仓库内完成，**不得新建第二份工程**。

---

## Level 1：看懂代码

**能力**：能说清"点连接 → 曲线动起来"这一条链路里每一跳。

**载体**：`docs/learn/architecture-walkthrough.md` + 源码行走。

**练习**：
1. 照着 architecture-walkthrough 第 3 节，**不看文档**在纸上画出 `connectDevice` 数据流（标注每跳的线程与信号名）；
2. 用 `grep -n "emit " src/services/*.cpp src/ui/pages/monitorpage.cpp` 找出所有信号生产点，逐一说出消费方是谁；
3. 说出 `main.cpp` 里 `app.exec()` 之前和之后各发生什么。

**验收标准**：能把数据流图默画出来，且能指出"队列投递"发生在哪一跳。

**答案索引**：
- 数据流总图：`docs/learn/architecture-walkthrough.md` 第 3 节
- 启动序列：`src/app/main.cpp`（4 行 `main()`）
- 布线现场：`src/ui/mainwindow.cpp` 构造（约第 40-90 行 connect 区）
- 消费端：`src/ui/pages/monitorpage.cpp:368` `onDataUpdated`
- 生产端：`src/services/acquisitionworker.cpp` 的 `emit pointsReady`

---

## Level 2：修改功能

**能力**：改一处配置/逻辑，影响运行行为，且不破坏测试。

**载体**：模拟设备 + 监控页 + 测试。

**练习**（任选 2 项）：
1. **改通道数**：`src/simulator/simulatordevice.h:155` 的 `m_channelCount = 4` 改为 6，重建 simulator，
   重跑 `tst_simulatordevice`——观察测试是否仍绿，并说明通道配置帧为何自动适配；
2. **改量程**：`src/simulator/simulatordevice.cpp` 里 4 通道的量程表改一档（如 ch0 0~100 → 0~200），
   连接后观察监控页仪表量程随之变化；
3. **改报警规则**：`src/domain/domainmodel_v2.h` 的 `AlarmRule`（上限/下限/条件）增删一条，
   运行 `tst_alarmengine` 确认报警评估逻辑仍正确。

**验收标准**：改动后 `ctest` 全绿；监控页/模拟设备行为符合预期；能说出改动波及的层。

**答案索引**：
- 通道数/量程来源：`src/simulator/simulatordevice.cpp`（通道配置应答）
- 配置链路消费端：`src/ui/pages/monitorpage.cpp:308` `onChannelConfigReceived`
- 报警引擎测试：`tests/unittests/tst_alarmengine.cpp`

---

## Level 3：独立增加模块

**能力**：在现有架构里新增一个独立功能块，不破坏分层。

**载体**：新增"串口设备"占位（P9 预留，不实现真实串口）。

**练习**：
1. 在 `src/simulator/` 仿 `SimulatorDevice` 新增 `SerialSimulator`（不连串口，用 `QSerialPort` 的
   模拟后端或直接复用 TCP 逻辑 + 标记"串口模式"）；
2. 让它产出 `DataPoint` 走**现有** DataService 总线（复用 `pointsReady` 通道），
   监控页无需改动即可显示；
3. 为新模块写 2 个测试（正常产出 / 无数据时），跑绿。

**验收标准**：新模块加入后 `ctest` 全绿，监控页出现新数据源，且 UI 层**零改动**。

**答案索引**：
- 现有设备的"模板"：`src/simulator/simulatordevice.cpp`
- 数据总线入口：`src/services/dataservice.cpp`（`onPointsReady`）
- 数据源可替换设计说明：`src/ui/pages/monitorpage.cpp:16` 注释

---

## Level 4：独立写 Qt 软件

**能力**：不依赖本项目代码，用 Qt Widgets 从零写一个小程序。

**载体**：SignalSlotDemo 思路（`src/app/signalslotdemo.cpp`）。

**练习**：写一个**定时计数器**：
- 一个 `QTimer` 每 500ms 发出计数信号，主窗口用 `QLCDNumber` 显示；
- 用 5 种连接方式各连一遍（可参考 SignalSlotDemo）；
- 加一个"暂停/恢复"按钮（`QTimer::stop/start`）；
- 用 `QTest` 写 2 个用例（点击启动后计数增长 / 暂停后不变）。

**验收标准**：程序可运行、逻辑在**非 UI 对象**里（可测），测试全绿。

**答案索引**：
- 五种连接写法模板：`src/app/signalslotdemo.cpp`
- QtTest 断言/等待：`tests/unittests/tst_signalslotdemo.cpp`
- 计时类：`QTimer`（事件循环依赖——结合 `qt-concepts.md` 第 1 节）

---

## Level 5：设计 Qt 架构

**能力**：给现有架构加一条新消费链路，保持分层不破。

**载体**：分层 + Model/View。

**练习**：给监控页数据加**第二消费者**：
1. 新增 `StatsWidget`（显示"当前值/均值/最大值"），数据来自 DataService 的 `dataUpdated`；
2. 方式 A：直接 `connect` 到 `StatsWidget` 的槽（简单）；
3. 方式 B（进阶）：新建 `StatsModel`（`QAbstractListModel`）+ `QTableView`，
   让 StatsWidget 只做 View——对比 A/B 的可测性差异；
4. 为 StatsModel 写 `tst_statsmodel`（喂数据 → rowCount/data 正确）。

**验收标准**：两种方式都跑通；能说出 B 为什么更适合单测；新增测试全绿。

**答案索引**：
- 现有 Model/View 范例：`src/ui/models/devicelistmodel.*`、`alarmeventmodel.*`
- View 挂接：`src/ui/mainwindow.cpp`（设备页/报警列表）
- 生产者-消费者结构：`src/ui/pages/monitorpage.cpp:348` 注释

---

## Level 6：线程 / 网络 / 协议 / 生命周期

**能力**：深入跨线程与协议健壮性，能解释本项目最"硬"的部分。

**载体**：V2 深度专题（模拟设备 + 异常注入 + 采集线程）。

**练习**：
1. **断线重连**：启动 `simulator -m Error -f Surge`，观察日志/监控页，解释
   `DeviceController` 状态机如何退避重连（对照契约 07 的状态表）；
2. **粘包/半包**：用 `-f Sticky` 制造粘包，解释为什么 `FrameParser` 状态机不丢帧；
3. **坏帧**：用 `-f CrcError` / `-f IllegalFrame`，解释重同步如何恢复；
4. **跨线程**：画出采集线程 → 主线程的信号投递，解释为什么要 `Q_DECLARE_METATYPE`。

**验收标准**：能对着契约文档逐条讲清"异常注入类 → 主机应对 → 测试断言点"。

**答案索引**：
- 异常注入类别：`docs/contracts/05-TCP链路与模拟设备契约.md` 第 7 节
- 状态机重连：`src/services/devicecontroller.cpp` + `docs/contracts/07-设备状态机契约.md`
- 协议解析：`src/protocol/frameparser.cpp` + `qt-concepts.md` 第 6 节
- 跨线程类型：`src/services/dataservice.cpp` 顶部 `Q_DECLARE_METATYPE`

---

## Level 7：读大型 Qt 项目 / 迁移实践

**能力**：把本项目当跳板，去读更大项目（Qt 官方 examples / 其他开源）。

**载体**：Qt 官方 example 对照。

**练习**：
1. 打开 Qt 自带 example（如 `qtdoc/examples/widgets/analogclock` 或 `charts`），
   对照本项目：找它的 `main()`、数据流、Model/View、线程用法，各写 2 句异同；
2. 把本项目的一个页面（如记录页）用**另一种风格**重写（QListView ↔ QTableView ↔ 自绘），
   体会 View 可替换性；
3. 写一篇 300 字复盘：DataScope 在哪些设计上借鉴了 WPF 的什么（用 `wpf-qt-mapping.md`）。

**验收标准**：产出复盘文档并提交，能讲出"Qt 项目怎么读"的方法论。

**答案索引**：
- WPF↔Qt 对照总表：`docs/learn/wpf-qt-mapping.md`
- 文档导航：`docs/README.md`
- Qt 官方 examples：Qt 安装目录 `D:\Qt\5.12.2\mingw73_64\examples\`

---

## 学习节奏建议

| 进度 | 建议 |
|------|------|
| 第一遍 | 只做 L1-L2，建立"看得懂 + 敢改"的自信 |
| 第二遍 | L3-L5，动手新增/重构，验证"架构是活的" |
| 第三遍 | L6-L7，深入底层 + 读大项目，形成方法论 |
| 全程 | 每次改动后跑 `ctest`——绿灯是底线 |

---

*下一篇：`README.md`（学习导航）——Level 1-7 路径图与文档索引。*
