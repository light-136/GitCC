# V3 架构草案审查报告 —— Principal Software Architect（攻击视角）

> **审查角色**：Principal Software Architect
> **审查对象**：`00-V3架构审查与草案.md`（V3 第一稿）
> **审查立场**：独立、挑剔、不盲从。目标不是认可，是推翻可推翻的假设。
> **日期**：2026-08-15
> **证据来源**：V2 源码 `DataScope/src/`（dataservice / devicecontroller / acquisitionworker / mainwindow / domainmodel_v2 / CMakeLists）

---

## 〇、先说结论（verdict）

**这份草案的"诊断"是尖锐且经得起源码验证的，但"处方"是过度矫正——它在批评 V2"为凑指标堆抽象"的同时，自己又堆出了一套更重的抽象。** 它可以作为 V3 的基础，前提是先砍掉 Application 层、合并 infra 库、并把"回放/实时切换"的竞态时序在数据流层说清楚。否则 V3 会变成"V2 门面问题换个马甲 + 多一层洋葱皮"。

---

## 一、我同意的点（简要，不奉承）

1. **对 V2 的三大诊断全部属实，我已逐条在源码里验证**：
   - `DeviceController`（自动重连状态机）在正式程序里是死代码——`mainwindow.cpp:56` 直接 `new DataService`，全文无一处 `DeviceController`；它只活在 `tst_devicecontroller` 里。（属实）
   - 双领域模型并存——热路径 `acquisitionworker.cpp:143` 用 V1 的 `domain::DataPoint`，而 `domainmodel_v2.h` 里的 `v2::DataPoint/AlarmRule/AlarmEvent` 只在 alarm/model 层被消费，是平行宇宙。（属实）
   - "连接/采集伪分离"——`dataservice.cpp:117` 的 `startAcquisition()` 只置一个 `m_acquiring=true` 再发个信号，不控制任何数据流；`mainwindow.cpp:234-235` 里 `connectTo` 与 `startAcquisition` 永远成对调用。（属实）
2. **"disconnected 双发"这条隐藏债务抓得很准**：`dataservice.cpp:146` 在 `stopAcquisition()` 里同步 `emit disconnected()`，随后 Worker 销毁 TcpClient 又触发 `onConnectedChanged(false)` → 再 `emit disconnected()`（`dataservice.cpp:182`）。这确实会导致"用户主动断开被误判为意外断线"。
3. **`domain` 库被 Qt Model 污染属实**：`CMakeLists.txt:69-78` 把 `devicelistmodel.cpp / alarmeventmodel.cpp`（QAbstractTableModel/ListModel）编进了 `datascope_domain`，违反"domain 零 Qt"的自我承诺。
4. **保留资产清单（第八节）判断准确**：FrameParser 流式解析、FaultInjector 纯函数注入器、SimulatorDevice 组件化、"数据源可替换思想"、元类型注册三要素一致——这五条是 V2 真正的金子，该留。
5. **方向性的正确判断**：V2 不直接作 V3 代码基础是对的；"domain 零 Qt"能顺带消灭 V2 最痛的一块——`Q_DECLARE_METATYPE`/`qRegisterMetaType` 散落不一致导致的跨线程参数丢失（`dataservice.cpp:46-53` 里为同一个类型注册两个名字就是症状）。

> 一句话：**诊断 9 分，处方 5 分。** 下面全部火力对准处方。

---

## 二、攻击主体（按严重度排序）

### P0-1：Application 层是"services 门面"换了个马甲，且把纯函数错当"用例"

**问题**
草案新增 V2 没有的 Application 层，用例列表是 `ConnectDeviceUseCase / StartAcquisitionUseCase / EvaluateAlarmUseCase / RecordSessionUseCase`。这些不是用例，是"单聚合、单调用、无事务、无补偿"的透传壳。更严重的是数据流图（第八节）里出现了一个 `[Application: 帧→DataPoint 用例]`——把"帧字节转 DataPoint"这种纯映射函数命名为"用例"，暴露了 Application 层正被当作"凡不是 UI、不是 Adapter 的东西都往里塞"的垃圾桶。

**为什么是问题**
V2 的 `DataService` 才 187 行，其"门面过重"的真凶不是缺一层用例编排，而是：(a) 连接/采集语义耦合（`startAcquisition` 是假的），(b) 报警规则硬编码在 View（`monitorpage` 的 0.85），(c) 记录同步写 CSV（`recordmanager.cpp:69` 在主线程槽内同步写文件）。这三条**没有一个靠 Use Case 层解决**——它们分别靠"连接/采集正交状态机进 domain"、"AlarmRule 领域对象"、"记录线程异步落盘"解决，而草案自己（第四节）已经把这三条写对了。Use Case 层是第四个、不解决任何已列问题的层。

**反例 / V2 佐证**
V2 的 `DeviceController::startAcquisition()`（`devicecontroller.cpp:86-89`）就是一行透传 `m_service->startAcquisition()`。草案要的 `StartAcquisitionUseCase` 和这一行透传**没有任何语义差异**，只是多了类名、接口、注入、fake。V2 的死代码已经证明：**没有真实编排的"控制器"层，最终只会变成"为了测试通过而存在的透传代码"**——这正是草案第二节第 1 条亲口批判 DeviceController 的原话。草案要用同样的方式再造一个。

**具体修改建议**
1. **砍掉 `dscope_application` 作为独立 target 和独立层。** 用例编排的落点是：领域状态机（`Connection`/`Acquisition` 各自的 `canTransition`，草案第四节已建模）+ UI 侧 Presenter（草案第十六节也已提出"每页一个 Presenter"）。这两个地方已经有编排能力，再加第三个编排层是双份账本。
2. 把"帧→DataPoint"这个映射函数放回它该在的地方：protocol 层或 domain 的 `DataPointFactory`/mapper，明确它**不是用例**。
3. 如果你坚持要"可替换 UI"这个理由才上 Application 层：当前只有一个 Qt Widgets UI，没有 QML/CLI 消费者，这个理由不成立。等真有第二个前端再抽不迟（那是重构，不是重写）。

---

### P0-2："只有一条数据入口" 与 "回放是独立 IDataBus 实现" 自相矛盾，回放↔实时切换的竞态时序未定义

**问题**
草案第八节同时写了两句互相打架的话：
- 关键点 1："只有一条数据入口——UI 只认 `IDataBus::subscribe()`"；
- 关键点 3："回放是独立的 IDataBus 实现——回放时真实采集暂停"。

如果是"独立实现"，那 UI 就得订阅**两个**总线并在两者间切换——这恰恰是 V2 的 bug 原样搬下去一层。

**为什么是问题**
V2 的真实 bug 我已在源码里确认：`mainwindow.cpp:66-67` 把 `DataService::dataUpdated` 连到 `MonitorPage::onDataUpdated`，`mainwindow.cpp:92-93` 又把 `RecordManager::replayData` 连到**同一个** `MonitorPage::onDataUpdated`。实时采集不停时点回放，两个源灌同一个槽，曲线混叠。草案正确诊断了这个 bug，但它的"修复"只是把"两个源在 View 层打架"改成"两个源在总线层打架"，**竞态本质没变**。

而且"回放时真实采集暂停"这句话把最难的部分当空气写掉了：**谁负责暂停？暂停的时序是什么？** 实时采集中途点回放，如果采集线程此刻刚产出一帧正在进总线，你"暂停"之前那一帧和回放的第一帧会同时到达 UI。必须有一个显式的原子切换协议：`暂停源A → drain(清空在途) → 原子换源 → 恢复`，否则回放开始瞬间仍会漏进一帧实时数据。草案对这套时序只字未提。

**具体修改建议**
1. 明确总线是**单一 IDataBus**，它的"数据源"是一个可切换的 `Source` 状态机（`LiveSource / ReplaySource`），而不是"两个 IDataBus 实现"。UI 永远只订阅一个 bus。
2. 把源切换写成状态机：`Live → (pause) → Draining → (swap) → Replay`，切换本身在 UI 线程串行化（Qt 的 QueuedConnection 天然保证同一线程不重入），配合"帧带源标记/会话 ID"让漏帧可被检测而不是静默混入。
3. 这与 P0-1 呼应：这个"源切换"才是 V3 里真正存在的一段**编排逻辑**，把它做成 domain 状态机，正好证明你不需要 Application 层——domain 状态机自己就是那个用例。

---

### P1-1：domain/protocol "零 Qt" 在当前现实下是"为了架构而架构"，收益是想象出来的

**问题**
草案第五、六节把"domain 和 protocol 不依赖 Qt"立为**最大红线**（"这是 V3 与 V2 最大的不同"），理由是可测、可迁 Linux、可被非 Qt 代码消费。

**为什么是问题**
逐条拆这个理由：
- **"可被非 Qt 代码消费"——当下唯一的非 Qt 消费者是 simulator，而 simulator 本身是 Qt 程序**（QTcpServer，`CMakeLists.txt:201` 链接 Qt5::Network）。所以协议层"零 Qt"今天没有任何非 Qt 消费者，这个收益是纯想象的。
- **"可测"——V2 已经用 QtTest 把 domain/protocol 单元测试跑通了**（`tst_domain_v2`、`tst_protocol`、`tst_channelcodec`，163 用例真实执行）。领域逻辑并不需要"零 Qt"才可测；需要的是"零 I/O、零事件循环"，而 `QString/QDateTime/QVector` 都不妨碍无 I/O 测试。
- **成本是真实的、且落在热路径和每一次 Model::data() 调用上**：每个字节都要 `QByteArray → std::vector<uint8_t>` 再转回来（草案第二十一节 #4 自己也承认这是未决问题）；`DeviceListModel`/`AlarmEventModel` 的 `data()` 每被渲染一次，`std::string → QString`、`std::chrono → QDateTime` 就要转换一次。Qt 的 Model/View 强制要求 `QVariant`，这条边界转换一分钱都省不掉。

草案自己在第二十一节把"domain 零 Qt 是否过度"列为待攻击点 #1，**却同时在第五、六节把它写成了"最大不同"的红线**——这是"先写结论、再挂一个'待攻击'的免责声明"，逻辑上不成立。

**具体修改建议**
1. **协议层：保留 Qt（`QByteArray`），不要 `std::vector<uint8_t>`。** 协议引擎的输入输出天生就是 `QByteArray`，换成 vector 只是把转换税转移给所有调用方。草案要的"零 Qt"在协议层没有任何收益。
2. **domain 层：可以零 Qt，但要说清楚真正的收益是什么**——不是"可测"（QtTest 已够），而是**消灭 `Q_DECLARE_METATYPE`/`qRegisterMetaType` 三要素一致这个坑**（V2 为此吃了真金白银的亏，`dataservice.cpp:46-53` 同类型注册两个名字就是病根）。如果 domain 是纯 C++，跨线程边界就由 adapter 层的 Qt DTO 显式承担，元类型注册集中到一处——这个收益成立。但代价是 `std::string ↔ QString` 在 Model 边界的转换，要诚实评估，别用"可迁移 Linux"这种不付钱的借口盖过去。
3. 如果评估后 domain 仍用 `QString/QDateTime`，那就**别在草案里宣称"零 Qt 是最大不同"**，改成"domain 不依赖 QtNetwork/QtWidgets，仅依赖 QtCore 值类型"——这是 V2 的 `domainmodel_v2.h` 已经做到的水平，别把已经达标的东西重新立成红旗。

---

### P1-2：9 个 CMake target 里，`infra_serial` 是空壳预留，`infra_storage` 是"职责过重"的原罪复活

**问题**
草案第六节列 9 个 target，其中：
- `dscope_infra_serial` 是"P9 预留"，**今天零代码**；
- `dscope_infra_storage` 把记录（FileRecordAdapter）、配置（ConfigAdapter）、日志（LogAdapter）三个**互不相关**的关注点塞进一个叫"storage"的库。

**为什么是问题**
草案第五节 5.2 亲口批判 V2"`services` 层职责过重（既有 TCP 又有状态机又有记录又有报警），边界不清"——这是对的。然后它在自己的 infra 层**原样复刻了同一个错误**：日志和配置跟"存储"有什么关系？这是把 V2 的 `datascope_infra`（logmanager + configmanager）原封不动搬进 V3 的 `infra_storage`，还硬塞进一个文件记录。批评别人的"职责过重"，自己换个名字照犯。

**关键认知错误：CMake target 是"构建产物边界"，端口可替换是"设计边界"，两者不能混为一谈。** "换 TCP→串口"靠的是 `IDevicePort` 接口多一个实现类，**不是**靠"TCP 和串口各建一个静态库"。你可以把 `TcpDeviceAdapter` 和 `SerialDeviceAdapter` 放在**同一个** `dscope_infra_device` 库里，端口照样可替换。反过来，拆成两个库，业务代码一样要依赖 `IDevicePort` 接口——拆库对"可替换性"没有任何增量贡献，只增加 CMake 维护成本和链接复杂度。

**具体修改建议**
1. **合并 infra：`dscope_infra_device`（tcp+serial，serial 等 P9 有了代码再加 .cpp）+ `dscope_infra_support`（config/log/record 或各自独立）。** 命名按**关注点**，不按**传输介质**。
2. serial 的 target 等 P9 真正动手那天再建。预留一个空库是"未来的死代码"，和 V2 的 `DeviceController` 是同一个病。
3. 修掉 5.2 的数字打架：标题写"5 个静态库"，正文列"6 个静态库（utils/infra/domain/protocol/services/simulator）"——`utils` 在 V3 的 9 个 target 里也凭空消失了，草案没说它去哪了（byteutils/timeutils 折叠进谁？）。

---

### P1-3：线程模型自相矛盾——铁律说 SPSC 单一写者，设计却是多写者

**问题**
草案第九节铁律 #4："共享数据不靠裸锁，靠有界队列 + 单一写者/单一读者（SPSC）"。但同一份草案里：
- 第九节表格写"每设备一个 I/O 线程"；
- 第二十节写"多设备：每设备一线程"；
- 第八节写"回放是独立 IDataBus 实现"（又一个生产者）。

**为什么是问题**
"每设备一线程"意味着 N 台设备 = N 个写者同时向一个 DataBus 灌数据，这是 **MPSC（多生产者单消费者）fan-in**，不是 SPSC。回放又引入第 N+1 个生产者。把"SPSC"当铁律写死，与"多设备每设备一线程"和"回放独立总线"两个设计直接冲突——这条铁律要么被违反，要么逼你为每设备建独立队列（那总线就不是总线了）。

**为什么现在就要说**
虽然 V3 第一阶段可能只有 1 TCP + 1 模拟器，但草案自己在第二十节把"多设备"列为预留扩展，并据此反推了"每设备一线程"。规格不能用一个"SPSC"去约束一个它自己许诺的"MPSC"未来。这是规格级的不自洽，不是实现细节。

**具体修改建议**
1. 把铁律 #4 改为：**总线是 MPSC 有界队列（多生产者、单消费者）**，并明确"生产者"包括每个设备的 I/O 线程 + 回放源；锁策略按 MPSC 设计（或每生产者一个 SPSC 队列、消费者轮询合并）。
2. 如果第一阶段确实只有单设备，就**别写"每设备一线程"和"多设备预留"**，或者明确标注"SPSC 仅适用于单设备阶段，多设备时升级 MPSC"。规格不能既许诺多设备又锁死单生产者。

---

### P2-1：依赖倒置的"仪式化"部分——不是所有端口都值得一个接口

**问题**
草案把依赖倒置当成全局铁律（"只有 Composition Root 知道具体 Adapter"），并排出一串端口：`IDevicePort / IDataSink / IClock / IConfigStore / ILogger / IDataBus / IDeviceManager`。

**为什么是问题**
依赖倒置的**真实需求**有两个判据：**(a) 确有第二个实现**（或确凿计划中的第二个），**(b) 接口稳定且廉价**。逐条对账：
- `IDevicePort`：**真值**。TCP / Serial / Simulator 三个实现（simulator 是测试基础设施，这是真需求），值得一个接口。
- `IClock`：**真值且廉价**。一个 `now()` 方法，测试里伪造时间价值巨大。
- `ILogger / IConfigStore / IDataSink`：**仪式**。各只有一个实现，且没有"测试需要 fake 掉日志"的动机（fake 日志得到的断言价值和成本不成比例）。为它们各写接口 + 注入 + fake，是"为了架构而架构"的样板。

**反例 / V2 佐证**
V2 已经给出了"正确抽象层次"的正面教材：`MonitorPage` 只认 `onDataUpdated` 一个入口（草案第五节第 5 条自己也承认这是对的）。这是一个**事件/观察者抽象**，不是"为每个依赖建接口 + Composition Root 组装"。V3 的正确姿势是继承这个层次，而不是把它拔高成"每个 Adapter 一个 Port"。草案从"V2 没抽象"直接跳到"最大抽象"，中间跳过了"够用即可"。

**具体修改建议**
只对满足判据 (a)+(b) 的端口做倒置：`IDevicePort`、`IClock`（可能加一个 `IDataBus` 因为回放）。`Logger/ConfigStore/RecordFile` 用**具体类 + 构造注入**（本身就是可替换的，因为你传对象，不传单例），不要套接口。这样 Composition Root 依然"知道一切"，但"知道"的是少数几个真正的接缝，不是九个 target 的每个角落。

---

### P2-2："少而精"的姿态 vs 全量清单，自我矛盾；且多处数字/措辞打架

**问题**
草案通篇以"反过度工程"自居（第二十一节连续自我设问"是否过度/是否仪式/是否教条"），却同时铺开了：4 层洋葱 + 9 个库 + `Result<T>` + 结构化日志 + JSON 场景文件 + benchmark + CI 打包 + 每页 Presenter。这是"一边喊少而精，一边开全量菜单"。

**为什么是问题**
V2 的死因不是"不够抽象"，是"抽象没接线 + 魔法数 + 教学代码污染"。V3 的当务之急是**把少数几个真问题改对**（连接/采集正交、报警规则进 domain、记录异步、单领域模型），而不是把架构图纸画到 9 层楼再一层层盖。草案把"反过度工程"当政治正确挂在嘴边，交付物却是更大的工程量，这在评审里是危险的——它让后续 7 个审查角色误以为"基础已经铺好了"，实则大部分 target 是空的。

**具体修改建议**
1. 给 V3 第一阶段一个**明确的最小切片**：`domain + protocol + device port（TCP + simulator）+ 单领域模型 + 连接/采集状态机 + 报警 domain 化`，把 `serial / 多设备 / 数据分析 / JSON 场景文件 / benchmark` 显式标为"后续阶段"，不建空 target。
2. 修掉文内矛盾：5.2 标题"5 个静态库" vs 正文"6 个"；第六节 `dscope_ui` 依赖列写"domain（只读）"，但红线 #3 又写"UI 只通过 application 层拿领域对象"——**Qt Model/View 强制 UI 直接持有领域对象**，这条红线与 Model/View 架构天然冲突，草案必须二选一：要么承认"UI 读 domain 只读模型"（这是对的、也是 Qt 的现实），要么就承认 Application 层在 Qt Model/View 下只剩"发命令"一条腿、变得更薄更冗余。

---

### P2-3：`Result<T>` 若不配套 `[[nodiscard]]`，就会重蹈"假装成功"覆辙

**问题**
草案第十一节用 `Result<T>` 替换裸 bool，声称解决 V2"失败但假装成功"。但示例代码里 `T value;` 是普通成员，`bool ok() const` 是普通方法，没有任何机制强制调用方检查 `ok()`。

**为什么是问题**
V2 的"假装成功"**不是 bool 的错，是调用方不检查**（`RecordManager::loadReplay` 0 帧返回 true、`connectTo` 二次请求静默吞没）。`Result<T>` 把 bool 换了个容器，调用方照样能 `r.value()` 不检查 `r.ok()`。草案在第二十一节 #5 自己也承认这是待攻击点，却没给答案。另外 `T value;` 要求 T 可默认构造，`Result<std::unique_ptr<X>>` 这类类型直接编译不过。

**具体修改建议**
`Result<T>` 必须：`[[nodiscard]]` 标注；`.value()` 在错误态下 `assert/terminate`（调试期强制暴露）；提供 `.value_or()`；错误不可静默忽略。若做不到"不可忽略"，就诚实说它只是"带上下文的 bool"，别宣称解决了 V2 的假装成功。

---

## 三、严重度总表

| 级别 | 编号 | 问题 | 一句话修改 |
|------|------|------|-----------|
| **P0 架构级错误** | P0-1 | Application/Use Case 层是 V2 门面问题换马甲，"帧→DataPoint 用例"是范畴错误 | 砍掉 `dscope_application`，编排落回 domain 状态机 + UI Presenter |
| **P0 架构级错误** | P0-2 | "一条数据入口"与"回放独立 IDataBus"矛盾，回放↔实时切换竞态未定义 | 单一 IDataBus + 可切换 Source 状态机（暂停→drain→原子换源→恢复） |
| **P1 设计缺陷** | P1-1 | domain/protocol "零 Qt"收益是想象的（唯一非 Qt 消费者 simulator 本身是 Qt） | 协议保留 QByteArray；domain 零 Qt 只认"消灭元类型注册坑"这一个真收益 |
| **P1 设计缺陷** | P1-2 | 9 个 target：`infra_serial` 空壳预留、`infra_storage` 是"职责过重"复活 | 按关注点合并 infra，serial 等 P9 有代码再建 |
| **P1 设计缺陷** | P1-3 | 铁律 SPSC 单一写者 vs "每设备一线程 + 回放"多写者自相矛盾 | 总线定为 MPSC，或明确 SPSC 仅单设备阶段 |
| **P2 过度工程** | P2-1 | 依赖倒置仪式化：ILogger/IConfigStore/IDataSink 各只有一个实现 | 只对 IDevicePort/IClock 等真接缝做接口，其余具体类+构造注入 |
| **P2 过度工程** | P2-2 | "少而精"姿态 vs 全量清单矛盾；UI"只经 application 拿 domain"与 Model/View 冲突 | 给最小切片；承认 UI 读 domain 只读模型 |
| **P2 过度工程** | P2-3 | `Result<T>` 无 [[nodiscard]]，照样可忽略错误 | [[nodiscard]] + .value() 错误态 assert |

---

## 四、verdict（一句话）

**可以作为 V3 基础，但必须先把 Application 层砍掉、把 infra 库按关注点合并、把"回放/实时切换"的竞态时序在数据流层写死——否则这份草案就是"用 9 层洋葱复刻 V2 的门面病"。**
