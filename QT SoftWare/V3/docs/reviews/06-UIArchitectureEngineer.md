# V3 UI 架构审查（UI Architecture Engineer）

> **角色**：UI Architecture Engineer（UI 界面架构师）
> **攻击对象**：`00-V3架构审查与草案.md` 第十六节「V3 UI 架构」及其与第二/四/八/十二/十八节交互的部分
> **立场**：痛恨"为分层而分层"的 UI。Qt Widgets 不是 MVVM 框架，每一层抽象都必须能说清"它挡住了什么真实成本"，否则就是仪式。
> **日期**：2026-08-15

---

## 一、同意点（简要）

下列判断我与草案一致，不再攻击，作为后续攻击点的共识基线：

1. **去教学代码是对的**。`signalslotdemo` / `demopage` / `monitorpage::onDemoTick`（4 通道正弦波数组 `m_center/m_amplitude/m_frequency`）编译进生产 exe，是 V2 的污染，草案第十六节第 3 条正确。
2. **自绘控件是真实资产、值得保留**。LineChartWidget / GaugeWidget / LedIndicator 是 V2 唯一有"自有绘制语义"的资产，丢弃是浪费。
3. **`removeFirst()` O(n) 是真实性能债**。`linechartwidget.cpp:66` 的 `while (m_data.size() > m_maxPoints) m_data.removeFirst()`，500 点满时每追加一点整体前移 499 个元素，草案第十八节第 2 条识别正确。
4. **颜色散落是真实问题**。`#16a085` 在 `main.qss` 出现 7 次，在 `linechartwidget.cpp`（`QColor(0x16,0xa0,0x85)`）与 `monitorpage.cpp`（LED 红绿）又各自硬编码，草案抽 token 的方向正确。
5. **报警阈值 0.85 必须从 View 层剥离**。`monitorpage.h:184 kAlarmRatio = 0.85` 是 V1 P0 问题的换位置复发，草案对此的诊断正确。

---

## 二、攻击点（主体，逐条硬碰硬）

### 攻击点 1：每页一个 Presenter/Controller 是仪式，不是架构

草案第十六节第 2 条原文：

> 每页一个 Presenter/Controller（瘦 UI）——UI 只渲染，业务逻辑在 application 层用例。

这条规格在 Qt Widgets 语境里**不成立**，原因有三层：

**第一层：Qt Widgets 没有绑定机制，Presenter 的全部价值要靠手写胶水兑现，而胶水没有复用性。**
WPF 的 MVVM 之所以能"瘦 View"，是因为有 `{Binding}` 数据绑定 + `INotifyPropertyChanged`，ViewModel 只改属性，View 自动刷新。Qt Widgets **没有这套东西**。Presenter 若要"只渲染"对应的好处，就必须为每个控件的每个更新方向手写 `connect(presenter, &Presenter::valueChanged, view, &View::setValue)` 和反向 `connect(view, &View::editFinished, presenter, &Presenter::commit)`。这套胶水：
- 不产生复用（每个页面都是 bespoke 的 connect 清单）；
- 不比 `QWidget::setText` 直接调用更短；
- 引入一个新类 + 一个头文件 + 一套测试，只为了"转发"。

**第二层：V2 的 MonitorPage 已经证明"直接连信号就够用"。**
`mainwindow.cpp:66-77`：

```cpp
connect(m_service, &DataService::dataUpdated,  m_monitorPage, &MonitorPage::onDataUpdated);
connect(m_service, &DataService::channelConfigReceived, m_monitorPage, &MonitorPage::onChannelConfigReceived);
```

监控页的数据流是**单向 push**：`数据源 → onDataUpdated → 控件 setText/setValue/appendPoint`。这里没有"用户输入 → 领域"的反向流，没有跨控件的状态联动，没有可测试的交互编排。给这种纯渲染页套一个 Presenter，唯一产出是：

```cpp
// Presenter（新类，纯转发）
void MonitorPresenter::onDataUpdated(const QVector<DataPoint>& pts) { m_view->render(pts); }
```

这就是草案第 21 节第 3 点自己担心的"services 门面问题换个马甲重来"的 UI 侧翻版。草案的 Application 层已经承担了编排（Use Case），UI 内部**再**套一层 Presenter，等于编排职责分两处、且 Presenter 会退化成 Application 层到 View 的纯转发器。

**第三层：什么时候 Presenter 是真的，什么时候是仪式——草案没有给出判据。**
判据很简单：**页面是否有"可独立于控件存在的交互逻辑"**。

- **DevicePage → 值得有 Presenter（或等价物）**。`devicepage.h` 的 `onAddDevice`（表单校验 → 入模型 + 持久化）、`onSelectionChanged`（同步按钮可用性）、`updateModelState`（连接信号 → 状态列回写）——这些逻辑**不依赖 QWidget**，抽出来可单测、可复用。这是真 Presenter/Controller 价值。
- **MonitorPage → 不需要 Presenter**。它的逻辑全部是"数据进来 → 驱动控件"，抽走后只剩转发。

**建议**：删掉"每页一个"的一刀切。改为**"有交互编排的页用 Presenter，纯渲染页用 Model/View + 直接信号槽"**。Qt 社区几十年惯例就是：QWidget 自带 View+Controller 二合一，signal/slot 就是 binding，`QDataWidgetMapper` 才是官方提供的"表单↔模型绑定"替代品。硬套 Presenter 是对 WPF 经验的错误迁移——用户是 WPF 背景（学习型项目），这恰恰是最容易照搬 MVVM 而不自知的地方，草案应明确点破而非顺应。

---

### 攻击点 2：Model/View "所有表格走 QAbstractItemModel" 是性能灾难级的一刀切

草案第十六节第 1 条原文：

> 所有列表/表格走 QAbstractItemModel，数据变化走 dataChanged，不手动刷新控件。

"所有"两个字是错的。必须按**数据频率**分流：

| 数据 | 变更频率 | 正确载体 |
|------|---------|---------|
| 设备列表 | 用户增删/状态翻转，秒~分钟级 | `QAbstractTableModel` + `dataChanged` ✅（V2 `DeviceListModel` 正例） |
| 报警事件列表 | 报警触发/恢复，事件级 | `QAbstractListModel` + `beginInsertRows` ✅（V2 `AlarmEventModel` 正例） |
| 通道实时值 | 10~30Hz × N 通道，**每帧都变** | ❌ 不该走 `dataChanged`，直绘控件 / `setText` 更优 |

**为什么高频值表走 Model/View 是灾难**：`emit dataChanged(topLeft, bottomRight)` 不会只"刷新一格"。它会沿 `QItemSelectionModel` → view 的 `viewport()->update(dirtyRegion)` 触发 delegate 的 `paint()`，而 `QTableView` 的 repaint 是**按可见区域**批发的（列宽自适应、滚动条范围重算、表头刷新都跟着走）。30Hz × 8 通道 = 每秒 240 次 dataChanged，每次可能触发整表 repaint——这是把"更新一个数字"放大成"重画一张表"。V2 的 `monitorpage.cpp` 里 `ui.valueLabel->setText(...)` 只重绘**那一个 QLabel**，方向反而是对的。

**草案不仅没区分，还更进一步把两类数据混进一条总线**：第八节数据流图里，`DeviceListModel` 被画在 `DataBus` 下游，仿佛"实时数据也进设备列表 Model"。设备列表是**低频配置数据**，报警事件是**事件流**，两者都不该与高频 `DataPoint` 流共用一条 `IDataBus` 通道。正确边界：

- `IDataBus` 只承载**高频数据点流**（有界队列、SPSC、背压，第十八节第 1 条的设计全部适用于且只适用于这里）；
- 设备列表走 `DeviceListModel` 的 `appendDevice/removeDevice/setDeviceState`（低频、用户驱动）；
- 报警事件走 `AlarmEventModel::appendEvent`（事件驱动、AlarmEngine 信号触发）。

**结论**：草案应把第十六节第 1 条改成——**"低频结构化数据（设备/报警）走 QAbstractItemModel + dataChanged；高频实时值走直绘控件，数据只进直绘控件的 setValue，不进 Model。"** 否则 V3 会在高频监控上自造一个 V2 都没有的性能问题。

---

### 攻击点 3：自绘控件"真实时间戳 + 阈值线 + 环形缓冲"三重加码，草案没有性能预算

草案第十六节第 4 条要求：时间轴用真实时间戳、阈值线、环形缓冲（去 O(n)）。这三条**方向都对，但叠加后的重绘成本草案完全没有核算**。基于 V2 实测代码：

**现状成本（`linechartwidget.cpp` 逐行）**：
- `appendPoint` 每次 `update()`（第 67 行）——一个点一次全量重绘请求。
- `paintEvent` **每次全量重绘**：`fillRect` 背景 + 5 条网格线 + 遍历 n 个点重建整个 `QPainterPath` + 标题 + 最新值。没有脏区（`QPaintEvent::region()` 被 `Q_UNUSED(event)` 丢弃，第 128 行），没有图层缓存。
- 抗锯齿已开（第 130 行）——500 点折线 + 高频重绘时抗锯齿是主要 CPU 消耗。
- 10Hz × 4 通道 = 每秒 40 次"遍历 500 点重建 QPainterPath + 抗锯齿绘制"。这还只是 V2 的低频演示；真实采集按草案第十八节第 3 条是 30Hz 节流，4 通道即 120 次/秒全量重绘。

**三重加码后的新增成本**：
1. **真实时间戳 X 轴**：从 V2 的"固定步进索引→像素"（`linechartwidget.cpp:178-184` 的 `step = width/(maxPoints-1)`）变成"值→时间戳→像素"映射。这要求绘制时做时间→X 换算，且**不同通道时间戳不对齐**时要么插值对齐、要么接受错位——绘制逻辑复杂度与 CPU 都上升。草案只说"时间轴用真实时间戳"，没提对齐策略（这是 V1 审查已指出的"帧无序列号/时间戳"问题在 UI 侧的后遗症）。
2. **阈值线**：每条阈值线 = 额外 `drawLine`，危险区 = 额外 `fillRect`。每次全量重绘都要重画这些静态元素。
3. **环形缓冲**：去掉 O(n) 正确，但环形缓冲 + 真实时间戳 = 读头/写头 + 时间戳单调性校验，遍历绘制要按时间序读，bug 面上升。

**要不要 QOpenGLWidget / QGraphicsView？——我的硬判断：现阶段都不要，但要做三件 QPainter 优化，草案一条都没提。**

- **QOpenGLWidget 不要**：4 通道、500 点、30Hz 的 QPainter 抗锯齿折线，现代 CPU 轻松消化。QOpenGLWidget 带来 OpenGL 上下文管理、驱动兼容、在 QWidgets 父级里的**离屏合成坑**（QOpenGLWidget 需要 FBO 合成，混排普通 widget 时闪烁/穿透问题多），是"为性能焦虑买单"。
- **QGraphicsView 不要（当前定位下）**：QGraphicsView 的价值在**多对象交互式场景图**（BSP 树、item 拾取、缩放平移）。实时滚动曲线是**单条折线的流式绘制**，不是场景图，BSP 树维护反而是纯负担。**但这里有一个草案必须回答的问题**：曲线定位是"实时滚动监视"还是"交互式分析"？若是后者（缩放/平移/十字光标拾取），QGraphicsView 或 Qwt 才是正解——草案用"自绘控件保留并提升"一句话把两个完全不同技术选型的问题糊过去了。
- **三件 QPainter 优化（草案应写进规格）**：
  1. **图层缓存**：背景 + 网格线 + 阈值线画到 `QPixmap` 缓存，只在尺寸/量程/阈值变化时重画；每帧只重画折线（`drawPolyline(QPolygonF)` 比重建 `QPainterPath` 更轻，且可 `reserve`）。
  2. **节流落点**：`update()` 合并（连续 append 期间 accumulate，QTimer 30Hz 统一 `update()`），而不是每点一次全量重绘。草案第十八节第 3 条说了"30Hz 节流"，但没说节流落在"数据进 UI 前"还是"widget 内部"——这决定成败，必须明确。
  3. **Gauge/LED 重绘纪律**：仪表盘指针每帧重绘比曲线便宜但同样需节流；**LED 是状态离散控件，只应在状态翻转时 `update()`**。V2 的 `monitorpage.cpp` 里 `ui.led->setColor(...)` 每帧都调，且报警红/正常绿（第 401-403 行）与 AlarmEngine 的判定是**两套独立实现**——这是攻击点 4 的不一致隐患，此处一并记下。

**结论**：草案"保留并提升"只写了"环形缓冲去 O(n)"一个词，把真实成本（图层缓存、脏区、节流落点、时间戳对齐、阈值线重绘、LED 离散重绘）全部留白。这不是提升，是给 V3 埋一个"曲线一多就卡"的坑。

---

### 攻击点 4：报警可视化——"阈值来自领域对象"是一句没有接口的空话，0.85 会三进宫

草案第十六节第 5 条：

> 报警可视化——曲线阈值线、仪表危险区、时间轴刻度。

结合第四节"报警规则是领域对象，阈值来自设备量程/用户配置"。方向对，但**UI 怎么拿到阈值，草案只字未提**。而 V2 的真实病灶恰恰在这里：

**V2 的阈值在三个地方以两种实现并存，必然不一致**：
1. `monitorpage.cpp:399` `onDataUpdated` 里 `const double alarm = rangeMax * kAlarmRatio` 驱动 LED 红/绿；
2. `monitorpage.cpp:169` `setupAlarmRules` 里 `rule.threshold = rangeMax * kAlarmRatio` 驱动 AlarmEngine；
3. `monitorpage.h:184` `kAlarmRatio = 0.85` 是这三者的共同魔法数来源。

LED 的判定（第 399 行 `value > alarm`）和 AlarmEngine 的判定是**两份独立代码**。今天它们碰巧都是 `rangeMax × 0.85`，明天任何一处改阈值比例，就会出现"LED 红了但报警事件没触发"或反之的**可视化与报警脱节**。草案把 0.85 挪进领域对象，如果只是挪位置（草案第 18 点自己都承认"只是换个位置"），而 UI 层**依然自己算一遍**，那就是 V1→V2→V3 三进宫。

**攻击核心：草案必须定义"阈值到 UI 的交付契约"，且要明确不是"每帧查询"。**
- **每帧查询（反模式）**：UI 在 `paintEvent` 里问 AlarmEngine"当前阈值多少"——耦合 + 每帧跨层调用，性能与边界都错。
- **订阅领域变化（过重）**：阈值变化频率 ≈ 配置变化频率（分钟~小时级），为它建一套订阅/发布机制是过度设计。
- **正确做法（一次性 set）**：阈值是领域对象 `AlarmRule` 的值，**在配置变化回调里一次性注入控件**：

```cpp
// 配置变化回调（onChannelConfigReceived 之类），非每帧
chart->setThresholdLine(rule.threshold);          // 曲线画阈值线
gauge->setDangerZone(rule.threshold, rangeMax);   // 仪表画危险区
```

控件的 `paintEvent` 只从自己的成员读阈值。这样：阈值来源单一（领域对象）→ 不是每帧查询 → 不是订阅 → 0.85 的根治方式是"报警规则由设备配置/用户配置定义，`rangeMax × ratio` 只是默认派生策略，作为 `AlarmRule` 的字段持久化，UI 只消费 `AlarmRule.threshold` 这一个值"。

**还必须指定一个 mapper 的归属**：`AlarmRule`（领域对象）不能直接泄漏进 `LineChartWidget`（绘制控件）——中间需要一个纯函数 `UI::toThresholdLines(const QVector<AlarmRule>&)` 把领域对象翻译成绘制参数。这个 mapper 属于 UI 层，可单测。草案对"报警可视化"的规格目前只有 4 个字加一句口号，等价于没设计。

---

### 攻击点 5：主题 token——"抽 token"在 Qt QSS 里没有落地机制，草案没给答案

草案第十六节第 4 条要"颜色/字体抽主题 token"。但 **Qt QSS 没有 CSS 变量**（不存在 `var(--primary)`），token 无法在 QSS 内部复用。草案说"抽 token"却没回答"token 怎么落到 QSS"——这是 V1 审查指出颜色散落后**连续第二次**复发的原因：不是没人想抽，是不知道在 Qt 里怎么抽。

**现状证据（颜色散落三处、两套系统各自维护）**：
- QSS（`main.qss`）：`#16a085` × 7、`#1e2a38`、`#3a4a5a`、`#2c3e50`、`#243341` 等几十个 hex；
- C++ 自绘（`linechartwidget.cpp`）：`QColor(0x16,0xa0,0x85)`、`QColor(0x15,0x20,0x2b,200)`、`QColor(0x3a,0x4a,0x5a)`；
- C++ 页面（`monitorpage.cpp`）：LED 的 `QColor(0xe7,0x4c,0x3c)` / `QColor(0x16,0xa0,0x85)`。

同一个主色 `#16a085` 在 QSS 和 C++ 各维护一套，换主题必然漏改。更糟的是 **V2 的"亮色主题"根本不是 token 切换，是卸载 QSS**（`mainwindow.cpp:324-332`：light 分支 `setStyleSheet(QString())` 退回系统默认，dark 分支重新加载）。这意味着 V2 只有"暗色=自定义 QSS、亮色=裸奔系统样式"两态，不是两套 token。草案的 token 系统必须解决"亮/暗两套 token 都有"，而不是把亮色继续定义为"无样式"。

**三条可选落地路线（草案必须三选一或给组合，否则"抽 token"是口号）**：

1. **运行时 QSS 模板 + 占位符替换（推荐为主）**：QSS 里写 `@primary`、`@bgPanel` 占位符，加载时用 `QString::replace` 换成 token 值再 `setStyleSheet`。这是 Qt 无 CSS 变量时的经典 workaround（等效 Sass 编译期替换，只是搬到运行时）。简单、够用；缺点是文本替换、易漏。

2. **C++ Theme 类作为唯一 token 源 + 自绘控件注入（推荐为辅，专治 C++ 硬编码）**：定义 `Theme` 对象持有 token（主色/背景/边框/字体/危险色），`LineChartWidget`/`GaugeWidget`/`LedIndicator` 构造时注入，`paintEvent` 读成员。彻底消灭 C++ 里的 hex。**这是根治 C++ 散色的唯一办法**，且 Theme 可单测、可序列化、可注入（符合草案 DI 精神）。

3. **Q_PROPERTY + 动态属性 + qproperty-xxx 选择器（草案自己点名的方向，我建议只作辅助）**：`Q_PROPERTY(QColor primary READ primary WRITE setPrimary)` + QSS 写 `qproperty-primary: #16a085`。注意方向：qproperty 是 **QSS → 控件**（从样式表读值喂给 Q_PROPERTY），不是 token → QSS。若把 source of truth 留在 QSS 字符串里，token 系统的单一来源就丢了，主题切换仍要文本替换。V2 已有一处用例（`main.qss:125 qproperty-alignment: AlignCenter`），说明机制可用，但它是"QSS 覆盖控件默认值"，与"token 单一来源"是两回事。

**推荐组合**：**单一 C++ Theme 源（可注入）→ 运行时 QSS 模板替换 + 自绘控件注入**。qproperty 只用于"QSS 覆盖某个控件局部默认值"的窄场景。草案至少要给出这条路线，并把"亮色主题是独立 token 集而非卸载 QSS"写死。

---

## 三、按严重度排序

| 排名 | 问题 | 严重度 | 一句定性 |
|------|------|--------|---------|
| 1 | 报警可视化的"阈值到 UI"交付契约缺失，0.85 会在 UI 层第三次复发（LED 与 AlarmEngine 双实现不一致） | **P0** | V1/V2 两版 P0 未根治的病灶，草案 UI 层只写"阈值来自领域对象"四个字，没定义接口与时机 |
| 2 | Model/View "所有表格走 QAbstractItemModel" 一刀切，未区分低频结构化（设备/报警）与高频实时值（通道值） | **P0** | 高频值表走 dataChanged 会自造 V2 没有的性能灾难，且 DeviceListModel 被错画进 DataBus |
| 3 | 主题 token 无落地机制，QSS 无 CSS 变量，颜色散落将三进宫，且 V2 亮色主题=卸载 QSS 不是真双主题 | **P1** | 草案喊"抽 token"不给机制，等于没说 |
| 4 | 自绘控件三重加码（真实时间戳+阈值线+环形缓冲）无性能预算：缺图层缓存/脏区/节流落点/时间戳对齐/阈值线重绘/LED 离散重绘 | **P1** | "保留并提升"只写了"环形缓冲去 O(n)"一个词，真实成本全留白 |
| 5 | "每页一个 Presenter" 是仪式化分层，MonitorPage 类纯渲染页套 Presenter 只是转发器，与草案自问的"Application 层是否多余"同源 | **P1** | 未给"何时需要 Presenter"判据，照搬 WPF MVVM 到 Qt Widgets 是错误迁移 |

---

## 四、Verdict

草案 UI 架构的**骨架判断全对**（去教学代码、保留自绘资产、去硬编码、低频数据走真 Model/View），但**关键处全是口号式规格**："每页一个 Presenter""所有表格走 Model/View""抽 token""报警可视化"四条都没有落地判据或机制。其后果恰好复刻 V1→V2 的历史：分层空转（Presenter 转发器）、性能自伤（高频数据走 dataChanged）、魔法数三进宫（0.85 与颜色 hex）。**UI 层缺一张"数据流 × 变更频率 × 渲染方式"的矩阵表**——没有它，"V3 的 UI 只是 V2 的换皮"。

**建议采纳的最小修改**（写进最终规格）：
1. 用"低频结构化走 Model/View、高频实时值走直绘控件"替换"所有表格走 Model/View"；`IDataBus` 只承载高频数据点流。
2. 定义阈值到 UI 的**一次性注入契约**：`setThresholdLine/setDangerZone` 在配置变化回调调用，非每帧、非订阅；`AlarmRule.threshold` 单一来源，UI 只消费。
3. 明确 token 落地：单一 C++ Theme 源 + QSS 模板占位符替换 + 自绘控件注入；亮色是独立 token 集。
4. 自绘控件补性能规格：图层缓存、脏区、节流落点、时间戳对齐策略、阈值线静态层、LED 离散重绘。
5. Presenter 判据：有交互编排的页（DevicePage）用，纯渲染页（MonitorPage）用 Model/View + 信号槽直连。

*本文件为 UI 架构攻击稿，供 Principal Architect 汇总。*
