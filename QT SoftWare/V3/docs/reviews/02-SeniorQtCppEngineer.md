# 02 · V3 架构草案攻击性审查 —— Senior Qt/C++ Engineer

> **审查角色**：Senior Qt/C++ Engineer（精通 Qt5 / C++17）
> **攻击焦点**：草案核心决策「domain 层零 Qt 依赖」及其连锁决策（元类型注册、对象生命周期、Result&lt;T&gt;、协议层零 Qt）
> **证据来源**：V2 源码 `src/domain/domainmodel_v2.h`、`src/domain/models.h`、`src/domain/models.cpp`、`src/domain/domainmodel_v2.cpp`、`src/services/dataservice.cpp`、`src/services/acquisitionworker.cpp`、`src/services/alarmengine.cpp`、`src/services/tcpclient.cpp`、`src/protocol/protocoltypes.h`、`src/protocol/frameparser.h`、`src/protocol/frameparser.cpp`、`src/app/main.cpp`
> **审查对象**：`00-V3架构审查与草案.md`
> **日期**：2026-08-15

---

## 0 · 一句话立场（先给结论）

草案把「零 Qt」错误地等同于「可测 / 可移植 / 可被非 Qt 消费」三个目标。真正的红线应该是 **「domain 依赖 QtCore，禁止 QtWidgets / QtGui / QtNetwork / QtSerialPort」**。domain 和 protocol 的可测性障碍从来不是 `QString` / `QVector` / `QByteArray`，而是事件循环与 I/O（`QTcpSocket` / `QTcpServer` / `QApplication`）——那些本就在 Adapter / UI 层，与 domain 无关。草案把「不依赖 I/O」偷换成了「不依赖 QtCore 容器」，为此付出的真实代价（隐式共享丢失、`QString↔std::string` 转换税、`QUuid` / 时区 / 中文显示名无处安放）远大于它想换来的那个「可能被非 Qt 消费」的臆想未来。更致命的是，草案自己的线程模型（元类型注册 + queued connection 传值）与「domain 零 Qt」**自相矛盾**，这是 P0。

---

## 1 · 同意点（简要）

| # | 草案主张 | 态度 | 说明 |
|---|---------|------|------|
| 1 | 一套领域模型（消灭 V1 + v2 平行宇宙） | 同意 | V2 的 `domain::DataPoint`（models.h）与 `domain::v2::DataPoint`（domainmodel_v2.h）并存、靠 ad-hoc 桥接，是真实债务，V3 必须只剩一套。 |
| 2 | protocol 不依赖 domain | 同意 | 字节契约不感知业务语义，`frameparser.h` 只 include `protocoltypes.h` 的正确分层应保留。 |
| 3 | 跨线程只传值对象、禁裸指针/引用 | 同意 | 这是 Qt 线程模型的正确规则，V2 的 `pointsReady(const QVector<DataPoint>&)` 就是正例。 |
| 4 | 错误码枚举化替代 `QString` 糊一锅 | 同意 | V2 的 `connectionError(const QString&)` 把连接失败 / CRC 错 / 解析错糊成一锅，确实无法分类降级。 |
| 5 | 依赖倒置（Ports & Adapters） | 同意方向 | 但「domain 依赖 QtCore」**不违反**依赖倒置——端口接口仍由 domain 定义、Adapter 实现，依赖方向不变。 |
| 6 | 元类型注册「集中」管理 | 同意「集中」，反对「无对象」 | V2 在 `dataservice.cpp` 里裸名 + 全限定名双注册 hack 是真实踩坑。但草案没分清「跨线程传值」与「QVariant 包装」两件事，见攻击点 A。 |

---

## 2 · 攻击点（主体）

### 攻击点 A（P0）——「集中元类型注册」与「domain 零 Qt」是草案自相矛盾，值对象如何到达 UI 线程没有被回答

**问题陈述**：草案第九节线程铁律第 5 条写「元类型注册集中在一个编译单元（metatype_registry.cpp）」，第七节红线写「domain 不 include 任何 Qt 头」。但元类型注册这件事存在的唯一目的，就是让 domain 值对象（`DataPoint` / `AlarmEvent`）能被 Qt 信号槽**跨线程传递**。草案通篇没有回答一个前置问题：**domain 值对象到底走不走 Qt 信号槽跨线程？**

**为什么两条路都是死结**：

- **路径一：走 Qt 信号槽（queued connection）**。I/O 线程 emit 信号、参数是 `DataPoint`，UI 线程槽里更新 Model。这要求 `DataPoint` 进入 Qt 元类型系统 → 需要 `Q_DECLARE_METATYPE`（编译期模板特化）＋ `qRegisterMetaType`（运行时注册）→ 两者都需要 `<QMetaType>` 头与编译期可见性 → **直接破「domain 零 Qt」红线**。
- **路径二：不走 Qt 信号槽（纯 std 有界队列）**。那「集中元类型注册」这条铁律就是**无对象的空规则**——没有任何类型需要注册。而且 UI 的 Model 更新必须发生在 UI 线程，只能二选一：
  - UI 线程用 `QTimer` 轮询 drain 队列 → 引入轮询延迟，违背草案自己「事件驱动」的定位；
  - I/O 线程 emit 一个**无参唤醒信号**，UI 线程被唤醒后再去共享队列取数据 → 退化成「共享数据 + 信号当门铃」，违背草案第九节第 2 条「跨线程只传值对象」的初衷。

**为什么这不是空想，V2 已经用血证明过**：`dataservice.cpp:46-53` 里对同一个类型做了裸名 + 全限定名**双注册 hack**：

```cpp
qRegisterMetaType<QVector<datascope::domain::DataPoint>>(
    "QVector<datascope::domain::DataPoint>");
qRegisterMetaType<QVector<datascope::protocol::ChannelConfigInfo>>(
    "QVector<datascope::protocol::ChannelConfigInfo>");
qRegisterMetaType<QVector<datascope::protocol::ChannelConfigInfo>>(
    "QVector<ChannelConfigInfo>");   // ← 同一个类型注册两个名字，纯属踩坑后的补丁
```

原因正是草案自己保留在「知识资产 #7」的那句话：**moc 规范化名 == 注册名 == Q_DECLARE_METATYPE 全限定名，三者必须一致**。换成 `std::vector<datascope::domain::DataPoint>`，moc 名里含 `std::` 前缀和 `::`，只会比 `QVector<...>` 更容易在「规范化名」上翻车——纯 std 类型**不会**让这个坑更浅，只会更隐蔽。

**建议**：
1. 草案必须先显式写死「domain 值对象从 I/O 线程到 UI 线程的机制」，再谈「零 Qt」。推荐机制就是 Qt 信号槽 + queued connection 传值对象，这天然要求 domain 类型进元类型系统。
2. 若坚持 domain 头不带 Qt：把 `Q_DECLARE_METATYPE` 放进 `dscope_ui` 或 `dscope_app` 的一个专用头（如 `domain_types_qt.h`）。但代价是每个用 `QVariant` 的 TU 都要 include 它，等于「domain 被 Qt 半污染」——**不如直接接受「domain 依赖 QtCore」**，把这个矛盾一次消解（见攻击点 B）。

---

### 攻击点 B（P0）——「domain 零 Qt」的代价被严重低估，正确红线是「依赖 QtCore，禁 Widgets / Network」

**问题陈述**：草案第五.1 把「零 Qt」与「可测 / 可迁移 Linux / 可被非 Qt 代码消费」三个理由绑定。**三个理由全部站不住**，而代价是实打实的。

**逐条拆穿三个理由**：

1. **「可测」论点不成立**。`QString` / `QVector` / `QDateTime` / `QByteArray` **不需要事件循环、不需要 QApplication**，在 gtest 里直接构造、断言即可。V2 的 `frameparser.cpp`（纯 QByteArray 逻辑）和 domain 单测就是活证据——它们依赖 QtCore 但照样是纯逻辑测试，一行 UI / 网络代码都没有。真正的可测性障碍是「依赖 QTcpSocket / QTcpServer / QtWidgets / 事件循环」，那些本就在 Adapter / UI 层，与 domain 无关。草案把「不依赖 I/O 和事件循环」偷换成了「不依赖 QtCore 容器」。

2. **「可迁移 Linux」论点不成立**。QtCore 在 Linux 上完全可用，`QString` / `QDateTime` 跨平台表现一致。**讽刺的是**：`std::chrono` 在 C++17 下**没有时区库**（`std::chrono::time_zone` 是 C++20 才有的）。而 `DataPoint.timestamp` 要做「本地时间格式化显示 + 写 CSV + 回放对齐」，C++17 下必须手写平台相关代码——Windows 的 `localtime_s` vs Linux 的 `localtime_r`。**这是「为了可移植性反而引入平台相关代码」**。

3. **「可被非 Qt 消费」是臆想的未来需求**。DataScope Studio 的主力消费者就是 Qt UI。为一个「可能」出现的非 Qt 消费者（CLI？嵌入式？），让整个主力项目在每条边界付转换税，是本末倒置。真要支持非 Qt 消费，QtCore 类型也跨平台、也能被普通 C++ 代码消费。

**草案没意识到的具体代价（都有 V2 源码佐证）**：

| 代价 | 证据 | 后果 |
|------|------|------|
| **隐式共享（COW）丢失** | `models.h:24-25`、`models.cpp:15` 明确依赖「QVector/QString 写时复制，拷贝 Device 只是引用计数 +1」；`dataservice.cpp:155-159` 返回 `lastData()` 快照、`acquisitionworker.cpp:150-153` `emit pointsReady(points)` 跨线程 queued connection | 换成 `std::vector` 后，跨线程传值从 **O(1) COW 变成 O(n) 深拷贝**。且 queued connection 是对参数做 const 引用拷贝（不是 move），`std::vector` 的 move 优势在此用不上。这**直接与草案第十八节「零拷贝 / 移动语义」矛盾**。 |
| **QString↔std::string 转换税** | V2 domain 的 `QString name/unit/message` 直接进 `QAbstractItemModel::data()` 的 display role、`setText`、日志，零转换 | 零 Qt 后每次 UI 显示都要 `QString::fromStdString` 深拷贝 + UTF-8→UTF-16 编码转换。高频路径（DataPoint 每秒数百上千，每个点的通道名/单位/报警消息）这个转换是持续的。中文文本「温度」「℃」不是 trivial 的字节拷贝。 |
| **QUuid 无处安放** | `domainmodel_v2.cpp:28` `DeviceId::create` 用 `QUuid::createUuid()` | C++17 标准库**没有 UUID**。零 Qt 后要么引 `boost::uuid`（第三方依赖，比 QtCore 还重），要么手写平台随机数（Windows `BCryptGenRandom` vs Linux `getrandom`，又是平台相关代码）。 |
| **状态显示名无处安放** | `domainmodel_v2.cpp:69-79` `deviceStateName()` 返回中文「已连接 / 连接中」等，domain 自带 | 草案第十一节说「QString 错误信息只在 UI 边界生成，ErrorCode→文案映射表在 UI 层」。这意味着 UI 层要维护 `enum→中文` 映射表，**UI 又变回「知道业务语义」的地方**，与草案第十六节「UI 只渲染，业务逻辑在 application 层」自相矛盾。状态显示名是领域语义，不该推给 UI。 |

**建议（折中方案，强烈推荐）**：

> **domain 依赖 QtCore（QString / QByteArray / QVector / QDateTime / QUuid / QSettings），禁止 QtWidgets / QtGui / QtNetwork / QtSerialPort。**

这条红线能拿到「零 Qt」想拿的**全部**好处（纯逻辑可测——无事件循环；跨平台——QtCore 全平台；不被 UI / 网络污染），同时保住 V2 已验证的正确性资产（COW 跨线程传值 O(1)、QDateTime 时区精度、QUuid、中文显示名内聚）。依赖倒置不受影响：Port 接口用 QtCore 类型声明，Adapter 实现端口，方向不变。

---

### 攻击点 C（P1）——「protocol 零 Qt」制造双深拷贝边界，且换 `std::vector` 没解决真正的 O(n)

**问题陈述**：草案第十三.4 用 `std::vector<uint8_t>` 替代 `QByteArray`，理由「可移植、可测、可被模拟器/测试直接消费」。三个理由都弱，且引入新代价、**没解决它想解决的性能问题**。

**证据与代价**：

1. **可测性是伪命题**。`QByteArray` 不需要事件循环，V2 的 `frameparser.cpp` 就是纯 `QByteArray` 逻辑，gtest 直接测。protocol 层的可测性障碍不是 `QByteArray`，而是 `QTcpSocket`——那在 adapter 层（`tcpclient.cpp`），本就不属于 protocol 层。草案把 adapter 的 I/O 依赖误当成了 protocol 的容器依赖。

2. **双深拷贝边界，与草案「零拷贝」自相矛盾**。`dscope_infra_tcp` 用 QtNetwork：接收侧 `tcpclient.cpp:104` `m_socket->readAll()` 返回 `QByteArray`，发送侧 `tcpclient.cpp:80` `m_socket->write(QByteArray)`。如果 protocol 层是 `std::vector<uint8_t>`：
   - 接收路径：`QByteArray` → `std::vector<uint8_t>`（逐字节深拷贝）；
   - 发送路径：`std::vector<uint8_t>` → `QByteArray`（逐字节深拷贝）。
   数据在「到达 adapter 第一站」和「离开 adapter 最后一站」各付一次深拷贝。而 QByteArray 方案里 adapter 直接 `readAll()` 喂 `feed()`，QByteArray COW 零拷贝。草案第十八节自己写「零拷贝 / 移动语义」，第十三节却制造了两道深拷贝，**前后矛盾**。模拟器也用 `QTcpServer` + `QByteArray`，同样在边界付转换税。

3. **换 `std::vector` 没解决真正的 O(n)**。草案第十八节说要「去 O(n) 操作」，但 `frameparser.cpp:98 / 115 / 142` 的 `m_buffer.remove(0, n)` 本身就是 O(n)（memmove 前移剩余字节）。`QByteArray::remove(0,n)` 和 `std::vector::erase(begin, begin+n)` **都是 O(n)**，半斤八两。真正的改进是换**容器语义**：`std::deque<uint8_t>`（头尾 O(1)）或「读游标 + 环形缓冲」（只移动 begin 指针、不搬字节）。草案把「换 QByteArray→std::vector」当成改进，**换错了方向**——`std::vector` 的头删除不比 `QByteArray` 好，真正该换的是 deque 或游标。
   - 另有 `mid` 的语义退化：`QByteArray::mid(2, len)` 返回 COW 子串视图（常量级 + 共享底层），`std::vector` 构造子串必须深拷贝 O(len)。`nextFrame` 每帧取 `data` 和 `crcInput` 两个 mid（`frameparser.cpp:128 / 133`），高频下 `std::vector` 是真 O(len) 拷贝。

4. **代码变难看（次要但真实）**：`mid` 变成 `std::vector<uint8_t>(m_buffer.begin()+2, m_buffer.begin()+2+len)`；`remove(0,totalLen)` 变成 `m_buffer.erase(m_buffer.begin(), m_buffer.begin()+totalLen)`。不算灾难，但可读性明显下降，且丢失了「字节容器」的语义表达。

**建议**：protocol 层**保留 QByteArray** 作为主容器（与 QtNetwork 边界零转换）。把「去 O(n)」的目标落到正确的地方——FrameParser 内部改成 `std::deque<uint8_t>` 或「读游标 + 环形缓冲」。若一定要「零 Qt」的字节缓冲抽象，自研一个 `ByteSpan` / `ByteBuffer` 薄封装，而不是裸 `std::vector` 直接暴露给解析代码。

---

### 攻击点 D（P1）——「业务对象不继承 QObject」与 Model/View 之间存在真空地带，V2 双模型问题会换皮复发

**问题陈述**：草案第十节「业务对象 ≠ QObject，用值语义 + unique_ptr/shared_ptr」，但第十六节「真 Model/View，QAbstractItemModel + dataChanged」。**两者之间没有桥**。草案批评 V2 的「两套模型平行宇宙」（models.h vs domainmodel_v2.h，且 `devicelistmodel` 编进 domain 库），但 V3 的方案很可能在 UI 层**再造一个视图模型 / DTO 层**——同样的病，换张皮。

**具体裂缝**：

1. **QVariant 包装**：`QAbstractItemModel::data()` 返回 `QVariant`。domain 的 std 值对象（`std::string` / `std::optional` / `std::chrono` / `std::vector`）进 `QVariant` 要 `QVariant::fromValue`，这又需要元类型注册 + `Q_DECLARE_METATYPE` 编译期可见性——**回到攻击点 A**。即使 Qt5.15 支持 `std::optional` / `std::chrono` 转 QVariant，这也是「std→QVariant→QString」的连续转换。

2. **变更通知真空**：domain 对象不继承 QObject、没有信号，那 `Device::state` 从 Connecting 变 Online 时，`DeviceListModel` 怎么知道要发 `dataChanged`？草案第十二节配置模型说「变更通知是信号 / 回调」，但没规定机制，只能三选一：
   - `std::function` 回调（observer）→ 回调可能在 I/O 线程触发 → UI 更新要跨线程 → 绕回攻击点 A；
   - UI 层 `QTimer` 轮询 → 延迟 + 违背事件驱动；
   - UI 层包一层 QObject 壳（ViewModel），壳订阅 domain 的 std 回调再转 Qt 信号 → **这就是「第二套模型」，V2 双模型问题的翻版**。

3. **V2 的教训被误读**。V2 的问题**不是**「domain 依赖 Qt」，而是「domain 里混进了 Model（`devicelistmodel` 编进 `datascope_domain`）＋ 两套 DataPoint 并存」。V3 的正确解法是「一套 domain 模型 + Model 放 UI 层」，而不是「domain 不继承 QObject」这个更激进、更伤及与 Model/View 协作的规则。值对象（DataPoint / ChannelConfig / AlarmRule）不继承 QObject 是对的——值对象本就不该有信号槽；但 domain 的**实体 / 聚合根**（`Device` 这种有状态、有生命周期、需要通知 UI 的）不提供**任何**通知机制，就是把「通知」这个必然需求硬推给 UI 层再造。

**建议**：
1. 值对象（DataPoint / ChannelConfig / AlarmRule）不继承 QObject——同意，纯 struct 值语义。
2. 有状态、需通知的实体（Device 聚合根 / AlarmEvent 流 / Session）应允许用 QtCore 的 QObject（信号槽）或至少 `std::function` 通知接口，并在草案里**显式规定**「domain 实体如何通知 UI」这条路径，而不是留白。
3. Model 是 UI 层的 `QAbstractItemModel`，但它**直接消费 domain 值对象**（通过 QVariant 包装），中间**不**再引入一层 DTO / ViewModel。这要求 domain 类型进 Qt 元类型系统——再次指向「依赖 QtCore」。

---

### 攻击点 E（P2）——手写 `Result<T>` 是造有坑的轮子，且不解决「错误可忽略」根因

**问题陈述**：草案第十一节手写 `Result<T>`（C++17 无 `std::expected`），理由是不返回裸 bool 假装成功。

**具体风险**：

1. **默认构造陷阱**：`Result<T>` 若用 `T value;` 值成员存储成功值，就要求 `T` 必须可默认构造（否则无法表达「失败态下 value 无意义」）。`std::optional` 的存储方式（对齐缓冲 + placement new）没有这个约束。草案的 `T`（DataPoint / AlarmEvent）现在都可默认构造，但这是**隐性约束**——未来加一个不可默认构造的 `T`（含引用的类型、要求参数构造的实体）就会编译失败。

2. **拷贝 / 移动 + 异常安全**：三成员（value + code + `std::string detail`）的手写拷贝 / 移动要正确处理 detail 的异常、value 的移动语义、ok 状态的同步，稍不留神就是悬空 / 双析构。`std::variant<T, Error>` 由标准库保证这些语义，`std::optional<T>` 亦然。

3. **与 Qt 的错误码重复映射**：`dscope_infra_tcp` 用 QtNetwork，Qt 本身给你 `error()` 枚举 + `errorString()`。`Result<T>` 要「翻译」`QAbstractSocket::SocketError` → `domain::ErrorCode`，又是一层手工映射表（V2 的 `tcpclient.cpp:94-99` 已经在做 `errorOccurred(m_socket->errorString())` 这种翻译）。`Result<T>` 没消除翻译，只是把翻译挪了个位置。

4. **不解决根因**：草案批评 V2「失败但假装成功」（`RecordManager::loadReplay` 0 帧仍返回 true）。根因是「返回 bool 且调用方忽略 / 不检查」，**不是**「没有 `Result<T>`」。`Result<T>` 如果不加 `[[nodiscard]]`、调用方不检查 `ok()`，一样会被忽略。C++17 的 `[[nodiscard]]` 可以加在 `Result<T>` 上，也可以加在普通 bool 返回上——「错误可忽略」是**调用纪律 + 编译器属性**的事，不是「造一个 Result 类型」的事。

**建议**：三选一，都比手写 `Result<T>` 划算：
- **`std::variant<T, Error>`**（C++17 有，语义明确，拷贝 / 移动 / 无默认构造陷阱由标准库保证，配 `std::visit` 消费）；
- **`std::optional<T>` + ErrorCode 出参 / 侧信道**（轻量，够用）；
- **接受 domain 依赖 QtCore，用 bool + error 枚举**（与 Qt 生态最顺）。
无论选哪个，都要配 `[[nodiscard]]` + 调用方 lint 规则，那才是治「假装成功」的药。

---

## 3 · 按严重度排序

| 严重度 | 编号 | 问题 | 一句话结论 |
|--------|------|------|-----------|
| **P0** | A | 元类型注册 + 跨线程传值 + 「零 Qt」三角矛盾 | 草案没回答「domain 值对象如何到达 UI 线程」，两条路都破红线，自相矛盾。 |
| **P0** | B | domain 零 Qt 是过度纯化 | 三个理由（可测 / 可移植 / 可消费）全站不住，代价是 COW 丢失 + 转换税 + QUuid / 时区 / 中文名无处安放。 |
| **P1** | C | protocol 零 Qt 制造双深拷贝 | 与草案「零拷贝」自相矛盾，且没解决 `remove(0,n)` 的真 O(n)——该换 deque / 游标，不是 vector。 |
| **P1** | D | 业务对象 vs Model/View 存在真空 | 变更通知无处落地，V2 双模型会以 ViewModel / DTO 层换皮复发。 |
| **P2** | E | 手写 Result&lt;T&gt; | 默认构造陷阱 + 与 Qt 错误码重复映射 + 不治「错误可忽略」根因。 |

---

## 4 · verdict

**「domain 零 Qt 依赖」是过度纯化，建议推翻，改为「domain 依赖 QtCore，禁止 QtWidgets / QtGui / QtNetwork / QtSerialPort」。**

理由一句话版：真正的可测性 / 可移植性障碍是事件循环与 I/O（在 Adapter / UI 层），不是 `QString` / `QVector`（QtCore 层）；草案把两者错误等同，导致付出「隐式共享丢失、`QString↔std::string` 转换税、QUuid / 时区 / 中文显示名无处安放」的真实代价，去换一个「可被非 Qt 消费」的臆想未来；且草案自己的线程模型（元类型注册 + queued connection 传值）与「零 Qt」自相矛盾，这个矛盾在写第一行代码前就必须先回答。

连带修正（必须随主线一起改，否则矛盾残留）：
1. **protocol 层保留 QByteArray**（与 QtNetwork 边界零转换）；「去 O(n)」的正确落点是 FrameParser 内部换 `std::deque` / 读游标，而非换裸 `std::vector`。
2. **跨线程传值机制显式写死为「Qt 信号槽 + 元类型注册」**，并说清 `Q_DECLARE_METATYPE` 与 `qRegisterMetaType` 的归属层（`dscope_ui` / `dscope_app`），不要留「metatype_registry.cpp 属于谁」的真空。
3. **domain 实体提供通知机制**（QtCore 信号槽或 `std::function`），并显式规定与 `QAbstractItemModel` 的桥接路径，禁止 UI 层再造 DTO / ViewModel。
4. **放弃手写 `Result<T>`**，改用 `std::variant<T, Error>` 或 `std::optional<T>` + `[[nodiscard]]`。

*本文件是 7 角色攻击的第 2 份，待 Principal Architect 汇总。*
