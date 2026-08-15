# 08 — 架构决策记录（ADR 列表）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

> **文档性质**：每个关键决策的可追溯记录。格式：背景 → 决策 → 理由 → 后果（正面/负面）。
> **约定**：任何推翻既有架构的变更，必须先在此新增一条 ADR 并更新 01，不得直接改代码。

---

## ADR-01：domain 依赖 QtCore，禁止 Widgets/Gui/Network/SerialPort

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案立"domain 零 Qt 依赖"为最大红线，理由是可测/可移植/可被非 Qt 消费。
- **决策**：推翻"零 Qt"，改为"domain 依赖 QtCore（QString/QByteArray/QVector/QDateTime/QUuid/QSettings），禁止 QtWidgets/QtGui/QtNetwork/QtSerialPort"。
- **理由**：三个理由全不成立——(1) 可测性障碍是事件循环与 I/O，不是 QtCore 容器（V2 的 `tst_alarmengine` 依赖 QtCore 照样纯逻辑测试）；(2) C++17 的 `std::chrono` 无时区库，去 Qt 反而引入平台相关代码；(3) 唯一非 Qt 消费者 simulator 本身是 Qt 程序。代价真实：COW 丢失（跨线程 O(1)→O(n)）、`QString↔std::string` 转换税、QUuid/中文显示名无处安放。
- **后果**：正面——保留 V2 已验证正确性资产，消灭元类型注册坑；负面——domain 与 QtCore 绑定，未来若真需非 Qt 消费需评估（当前无此需求）。

## ADR-02：砍掉 Application/Use Case 层

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案新增 Application 层，用例是 `ConnectDeviceUseCase` 等。
- **决策**：删除 `dscope_application` target。编排逻辑落回 domain `Device` 聚合根状态机＋UI 层 Presenter（仅限有交互编排的页）。
- **理由**：这些"用例"是单聚合、无事务、无补偿的透传壳；V2 的 `DeviceController::startAcquisition()` 就是一行透传，证明"无真实编排的控制器层最终变成透传死代码"。
- **后果**：正面——少一层、少一组空接口；负面——"源切换"这类真编排逻辑必须显式建模为 domain 状态机（见 ADR-05）。

## ADR-03：固定 N 个 I/O 线程＋socket 多路复用（否决"每设备一线程"）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案把"每设备一线程"写进扩展路线。
- **决策**：固定 N（默认 1，按核数可设 2~4）个 I/O 事件循环线程承载所有设备 socket，设备由分片器按 `deviceId` 哈希归属。
- **理由**：Qt socket 事件驱动非阻塞，单线程天然多路复用；场景数据量（20Hz×4）不需并发 CPU；"每设备一线程"的资源成本（栈/TCB/EventDispatcher ×N）与关停复杂度（V2 最脆代码复制 N 份）远超收益。
- **后果**：正面——扩展时不随设备数增线程；负面——CPU 密集解析未来需加工作线程池（N:M），但那是分析阶段的事。

## ADR-04：单一 IDataBus＋可切换 Source 状态机（否决"回放是独立总线"）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案写"只有一条数据入口"与"回放是独立 IDataBus 实现"自相矛盾。
- **决策**：UI 只订阅一个 `IDataBus`，数据源是可切换的 `Source` 状态机（LiveSource/ReplaySource），切换时序 `暂停→drain→原子换源→恢复`，帧带源标记。
- **理由**：V2 的 bug 是实时采集不停时点回放，两个源灌同一槽曲线混叠；草案的"独立总线"只是把"两个源在 View 打架"改成"在总线打架"，竞态本质未变。
- **后果**：正面——竞态被显式状态机消除；负面——回放功能因此列入后续阶段（第一阶段先不实现回放，只把 Source 状态机接口定义好）。

## ADR-05：报警引擎挂在丢帧之前的完整流

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案数据流图把 AlarmEngine 画在 DataBus 之后，与"有界队列会丢帧"自相矛盾。
- **决策**：报警评估在采集线程内、数据入队列前同步完成。
- **理由**：报警需要连续数据判趋势（连续 N 点超阈/斜率/突变），drop-oldest 会在序列挖洞导致漏报/误报。
- **后果**：正面——报警正确性从设计第一天有保证；负面——报警评估占用采集线程 CPU，需保证评估本身无 I/O、无阻塞（纯逻辑）。

## ADR-06：帧格式升级带协议版本号＋版本协商

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案给帧头加 SEQ(4B)＋时间戳，但没定义新旧帧如何共存。
- **决策**：帧头增加 1 字节 version 字段；定义完整新帧布局（逐字段/字节序/CRC 新覆盖范围/时间戳编码）；版本来源采用"帧头 version 字节"（显式、逐帧自识别）。
- **理由**：帧格式上线后不能单方面改，否则旧设备/模拟器的 FUNC/CMD 会错位读成 SEQ 高位字节，最坏静默误解析成新帧。
- **后果**：正面——新旧帧可共存识别；负面——帧头膨胀，CRC 覆盖范围变更需同步更新契约文档与 FrameBuilder/Parser。

## ADR-07：IDevicePort 分 transport/framing 两层（否决单端口压扁串口）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案一个 IDevicePort 同时表达 TCP 与串口。
- **决策**：`ITransport`（open/read/write/close＋能力位）之上加 `IFraming`（TCP=流式 SOF+LEN；串口=帧间隙定界）。串口特有配置（数据位/校验位/停止位/流控/半双工）走 `ConnectionParams` 的 tagged union，不让 domain 层为串口开接口。
- **理由**：TCP 与串口差异在帧定界与链路状态（串口 RS485 需帧间静默超时＋方向切换＋DSR/CD 断线检测），单端口会把串口语义压扁成"像 TCP 的串口"。
- **后果**：正面——串口语义有处安放，domain 不被物理层污染；负面——端口抽象分两层，接口数量增加（但这是真接缝，非仪式）。

## ADR-08：ErrorCode 分类＋致命性维度＋错误阈值策略

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案只有一维 ErrorCode 枚举＋"错误计数器"，无阈值策略。
- **决策**：ErrorCode 带"致命性"维度（瞬态/严重/致命）；断开时机定为"连续 N 错 或 持续 T 秒无有效帧"；CRC 错＝瞬态丢弃不重发，非法帧＝严重丢弃告警不立即断开。
- **理由**：单次脏数据断开会放大成重连风暴；计数器是死的，没有策略。
- **后果**：正面——错误处理有降级梯度，不再"一错就断"；负面——N/T 两个阈值需作为配置项暴露（不硬编码），测试需覆盖阈值边界。

## ADR-09：错误模型用 `std::variant<T, Error>`＋`[[nodiscard]]`（否决手写 Result<T>）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案手写 `Result<T>` 声称解决"失败假装成功"。
- **决策**：弃用手写 `Result<T>`，改用 `std::variant<T, Error>`（或 `std::optional<T>`＋ErrorCode 侧信道）＋`[[nodiscard]]`。
- **理由**：手写 `Result<T>` 有默认构造陷阱（要求 T 可默认构造）、拷贝/移动异常安全风险、与 Qt 错误码重复映射，且不解决根因（调用方不检查照样忽略）。`std::variant` 由标准库保证语义，配 `std::visit` 强制处理两分支。
- **后果**：正面——错误不可忽略（`[[nodiscard]]`＋`bad_variant_access`）；负面——需团队统一 `std::visit`/`std::get_if` 消费风格。

## ADR-10：单一版本源＋windeployqt 配置期硬失败＋deploy 出库

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：V2 版本号 5 处硬编码（simulator 竟写 2.0.0）、windeployqt 静默失败、deploy 20MB DLL 进 git。
- **决策**：版本只改 `project(VERSION)`，`configure_file` 生成 `version.h`＋`version.rc` 自动传播；`find_program(windeployqt REQUIRED NO_DEFAULT_PATH)` 找不到就 `FATAL_ERROR`；`deploy/` 进 `.gitignore`，发布包是带版本号 zip。
- **理由**：可复现构建的三大支柱（依赖锁定/工具链校验/产物出库）V2 一个没立。
- **后果**：正面——改版一处、构建失败即暴露、产物不入库；负面——需要 `release.ps1` 脚本＋`buildinfo.txt` 构建记录。

## ADR-11：UI 数据流按"频率"分流（否决"所有表格走 Model/View"）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：草案要"所有列表/表格走 QAbstractItemModel"。
- **决策**：低频结构化（设备列表/报警事件）走 `QAbstractItemModel`＋`dataChanged`；高频实时值走直绘控件 `setValue`，不进 Model、不进 DataBus。
- **理由**：高频值表走 dataChanged 会触发整表 repaint（每秒 240 次），把"更新一个数字"放大成"重画一张表"。
- **后果**：正面——避免自造 V2 都没有的性能问题；负面——需明确"哪些数据算低频、哪些算高频"的判据（频率阈值，如 >10Hz 走直绘）。

## ADR-12：报警阈值单一来源＋UI 一次性注入（0.85 三进宫根治）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- **背景**：V2 阈值在三个地方以两种实现并存（LED 判定 vs AlarmEngine 判定各自算 `rangeMax×0.85`）。
- **决策**：`AlarmRule.threshold` 是唯一来源；UI 在配置变化回调里一次性注入（`chart->setThresholdLine(rule.threshold)`），非每帧查询、非订阅；`rangeMax×ratio` 只是默认派生策略，作为 AlarmRule 字段持久化。
- **理由**：每帧查询＝耦合＋每帧跨层调用；订阅机制＝过度设计（阈值变化频率≈配置变化频率）。一次性 set 才是正确抽象。
- **后果**：正面——LED 与 AlarmEngine 永远一致；负面——配置变化时需触发一次注入回调（在 onChannelConfigReceived 里做）。

---

*以上 12 条 ADR 覆盖 V3 所有关键决策。新增架构变更必须走"新增 ADR→更新 01→Code Review"流程。*
