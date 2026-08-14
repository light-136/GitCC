# 《Qt 学习地图 · 02：Qt 界面布局与 QSS 样式（P5）》

> 文档编号：03-02　版本：v0.1（**设计意图稿**，主 Agent 正在集成 P5 页签布局，QSS/qrc 落地以集成后为准）
> 建立阶段：P5　修订：随阶段更新
> 作者：主 Agent（规划与最终集成）＋ D3（教学专项 Agent，本文档）
> 面向读者：熟悉 C#/WPF、不懂 Qt 的开发者（正在通过 DataScope Studio 学习 Qt）
> 配套工程：DataScope Studio（Qt 5.12 Widgets + C++17 + CMake）
> 配套代码（本阶段设计意图）：`DataScope/src/ui/mainwindow.*`（P5 重构为页签布局）、`DataScope/src/ui/pages/`（五个业务页面）、`DataScope/resources/resources.qrc`
> 参考前置：`docs/04-WPF转Qt对照指南/01-C++与Qt核心概念对照.md`、`docs/04-WPF转Qt对照指南/02-Qt对象模型与内存管理.md`

---

## 0. 这份文档怎么读

本文属于 `docs/03-Qt学习地图/`（Qt 知识 → 模块 → 源码 → WPF 对照的持续地图），编号 03-02。它承接 03-01（日志与配置基础设施），把 UI 层的两件配套的事一次讲透——**布局（Layout）** 与 **QSS 样式**。为什么这两件事要放一起？因为它们在 WPF 里就是一对：XAML 写 `Grid`/`StackPanel` 负责"放哪、多大"，`Style`/`Trigger`/`ResourceDictionary` 负责"长什么样"。Qt 里对应的是 **布局管理器 + `setStyleSheet()` 全局样式表**——一个管几何，一个管外观。

每一节沿用 03-01 的约定结构：**一句话类比 → 机制讲解 → 为什么不能简单照搬 → 在 DataScope 里的落地**，少堆 API、多讲为什么。

**约定**：英文术语首次出现给中文注释；代码块一律 `cpp`，资源文件用 `xml`，WPF 对照用 `csharp`；注释用简体中文。本文所有代码是**教学用的设计意图稿**——写法参考 Qt 5.12 真实 API，但页签页面与 `.qrc` 由主 Agent 集成；落地时以 `src/ui/`、`resources/resources.qrc` 的最终代码为准。

**阅读前置**：你已经会用 `QVBoxLayout`（P4 的 `mainwindow.cpp` 第 69 行），会 `connect` 信号槽（对照指南 01 第 5 节），知道 QWidget 挂 parent 进对象树（对照指南 02 第 2 节）。本篇把这三个知识在"真实窗口布局"里串起来。

---

## 1. 本节为什么重要：UI 是工业软件的"脸面与操作台"

> 一句话类比：布局是"操作台的电气布线图"——每个仪表、旋钮放在哪一格；QSS 是"钣金喷漆"——什么颜色、什么质感。**布线图错了，操作员够不到旋钮；喷漆错了，昼夜班看不清读数。**

WPF 里这两件事你已经做得非常顺手：

- **布局**：`Grid` 配 `RowDefinitions`/`ColumnDefinitions`，`*` 号弹性分配行高列宽；`StackPanel` 一字排开；`DockPanel` 停靠。设计器里拖一拖，XAML 自动生成。
- **样式**：`Style TargetType="Button"` 定颜色圆角，`Trigger`/`DataTrigger` 响应鼠标悬停、选中状态；`ResourceDictionary` 把一套主题收进一个 XAML 文件，`App.xaml` 合并引用，全局生效。

为什么工业多通道采集软件的 UI **必须先讲布局与样式**？

1. **分辨率与字体是工业现场的常态敌人**。客户现场可能是 1366×768 的老工控机、也可能是 4K 大屏；Windows 缩放可能是 100% 也可能是 150%。**绝对定位（`setGeometry(x,y,w,h)`）在这种环境下就是灾难**——字一放大、控件就重叠。布局管理器 + 弹性伸缩才能"自动适应"。
2. **同一套界面要表达"多工况"**。监控页、日志页、配置页、报警页……五页内容密度天差地别，必须用**页签（QTabWidget）**收进一个主窗口，否则窗口会变成一堵墙。
3. **深色主题是工业软件的刚需**。车间光照强，白底刺眼；夜间值班，亮屏晃眼。WPF 里换主题靠切 `ResourceDictionary`；Qt 里靠**换一套 QSS 字符串再 `setStyleSheet()`**。这套机制 P5 不建立，P14 监控页的曲线/仪表控件就没有统一的外观地基。

这就是 P5 阶段的任务：把主窗口从 P4 的"信号槽教学舞台"重构成**五个页签 + 全局深色 QSS** 的真实工业界面。本文档就是把这套东西翻译成"怎么理解、怎么写、为什么"。

---

## 2. WPF → Qt 布局对照总表

| WPF / C# | Qt / C++ | 一句话差异 |
|----------|----------|-----------|
| `StackPanel`（纵向） | `QVBoxLayout`（纵向盒式布局） | 都是"排队"，Qt 的 stretch 控制谁分得多 |
| `StackPanel`（横向） | `QHBoxLayout`（横向盒式布局） | 同上，方向横过来 |
| `Grid`（`RowDefinitions`/`ColumnDefinitions`） | `QGridLayout`（行列网格） | Qt 的"行/列"各自有 stretch 与对齐 |
| `DockPanel` | `QDockWidget` / 布局嵌套 | 无直接等价；见 3.4 布局嵌套与 4 页签 |
| `TabControl` / `TabItem` | `QTabWidget` / `addTab` | 一个 QTabWidget 装多个 QWidget 页 |
| `ScrollViewer` | `QScrollArea` | `scroll->setWidget(page)`，内容可滚动 |
| `Margin` / `Padding` | `layout->setContentsMargins` / `setSpacing` | WPF 是控件属性；Qt 是**布局**的属性 |
| `HorizontalAlignment` / `VerticalAlignment` | 对齐旗标 `Qt::AlignCenter` 等 | `addWidget` 的第 3 参；stretch 分配后多余空间才对齐 |
| `*` / `2*`（Star 尺寸） | stretch factor（`addWidget` 第 2 参） | **最像但不是一回事**，见 3.1 |
| `VisualBrush` / `Background` | QSS / `QPalette` | QSS 是声明式字符串；QPalette 是代码式配色 |
| `Style TargetType="Button"` | QSS 选择器（`QPushButton`、`#objectName`、`.class`） | 选择器语法更像 CSS |
| `Style.Triggers`（`Trigger`/`DataTrigger`） | QSS 伪状态 `:hover` / `:checked` / `:disabled` | 样式内响应状态变化，见 5.3 |
| `ResourceDictionary`（`x:Key`） | QSS 文件 + `resources.qrc` 资源系统 | 资源编译进 exe，见 6 |
| 手动 `LoadResource` / App.xaml 合并字典 | `qApp->setStyleSheet()` 加载 QSS | 一行代码全局换肤 |
| `Canvas`（绝对定位） | `setGeometry(x,y,w,h)` | 工业软件不推荐，见 3.4 |

**逐行讲解**（挑容易误解的几行）：

- **布局对象 belongs to 谁**：WPF 里 `Grid` 是"容器控件"，你往 `Children` 里加子元素；Qt 里 `QHBoxLayout` **不是控件**（不继承 QWidget），它是 `central` 这个 QWidget 的**布局器**。构造时 `new QVBoxLayout(central)` 就把布局"安"到了 central 身上，子控件再 `layout->addWidget(child)`。**布局管 child，child 的 parent 仍是 central**（对照指南 02 第 2 节的对象树关系：所有 child 都挂在 central 这棵树上，布局只是"摆位置的人"）。
- **Margin/Padding 的位置变了**：WPF 的 `Margin` 写在控件上；Qt 的 `setContentsMargins(l,t,r,b)` 写在**布局**上（布局与容器四边的间距），控件间距是 `layout->setSpacing(n)`。控件自己也有 `setContentsMargins`（如 `QLineEdit` 内部文字与边框的间距），别混淆。
- **对齐是"分配后的定位"**：WPF 的 `HorizontalAlignment="Center"` 是"在可用空间里居中"；Qt 的 `Qt::AlignCenter` 是 `addWidget(widget, stretch, Qt::AlignCenter)` 的**第 3 参数**——先按 stretch 分配格子的尺寸，如果格子的实际尺寸大于控件 sizeHint，再按对齐旗标把控件摆在格子内。**stretch 决定格子多大，对齐决定控件在格子里站哪。**

---

## 3. 布局机制深入：弹性伸缩模型

> 一句话类比：WPF 的 `*` 是把一张饼**按比例切开**（先切饼再装盘）；Qt 的 stretch 是"每人先端走自己的那份（sizeHint），**剩下的饼**再按比例分"（先装盘再分剩余）。

### 3.1 stretch factor vs WPF Star sizing —— "最像但不是一回事"

**代码对比**

```csharp
// WPF：Grid 两行，1:2 分配可用高度
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="1*"/>   <!-- 占可用高度的 1/3 -->
        <RowDefinition Height="2*"/>   <!-- 占可用高度的 2/3 -->
    </Grid.RowDefinitions>
</Grid>
```

```cpp
// Qt：纵向布局，stretch 3 与 1 分配"额外空间"
auto *layout = new QVBoxLayout(central);
layout->addWidget(m_logView, 3);   // 日志区：多拿额外空间
layout->addWidget(m_statusBar2, 1); // 辅助条：少拿
```

**机制讲解**

WPF 的 `*` 是**精确比例**：`1*` 行拿到的是"可用高度 × 1/3"，与内容大小无关。Qt 的 stretch **不是**直接比例，它的计算分两步：

1. **先满足每个控件的 `sizeHint()`（期望尺寸）**。每个 QWidget 都有 sizeHint：`QPushButton` 按文字+边距算，`QPlainTextEdit` 有默认大小，普通 `QWidget` 有内建默认。布局先把这些"底线"都满足。
2. **剩余空间（可用尺寸 − 各控件 sizeHint 之和）再按 stretch 比例分**。`stretch=0` 表示"我不参与分剩余"（只拿 sizeHint）；`stretch=3` 与 `stretch=1` 意味着剩余空间按 3:1 分。

**关键推论（也是最大的坑）**：

- **stretch 比例 ≠ 最终尺寸比例**。如果控件 A 的 sizeHint 是 200px、stretch=3，控件 B 的 sizeHint 是 50px、stretch=1，窗口高 400px 时：剩余 150px，A 得 112.5、B 得 37.5 → 最终 A=312、B=87，比例约 3.6:1，不是 3:1。**WPF 里 1:2 就是 1:2；Qt 里 stretch 只分"剩菜"。**
- **stretch=0 不是"固定不长大"**。默认 `addWidget(w)` 的 stretch 是 0，控件只拿 sizeHint。但如果控件自己的 `sizePolicy` 是 `Expanding`（见 3.2），它**仍然会吃掉额外空间**——stretch=0 管的是"布局分配的份额"，sizePolicy 管的是"我自己想不想长大"。两个机制叠加，刚开始会晕，记住：**stretch 是布局说的"你想长大吗"，sizePolicy 是控件说的"我允许自己长大吗"，两个都要点头才长大。**

**在 DataScope 里的落地**：P5 监控页布局——曲线区（P14 的控件）给大 stretch，日志区给小 stretch。写完 `addWidget` 后**先跑一次，拉伸窗口看谁在变、谁纹丝不动**，再回来调 stretch。这是学布局的"最小实验循环"。

### 3.2 sizePolicy —— 控件自己的"生长意愿"

> 一句话类比：`QSizePolicy` 是控件的"体检表"，横向、纵向各填一行：**能缩吗？能长吗？想长吗？**

**代码对比**

```csharp
// WPF：Alignment 只管对齐，尺寸靠 Height/Width 或 Star
Button b = new Button { HorizontalAlignment = Stretch, Height = 30 };
```

```cpp
// Qt：setSizePolicy(水平策略, 垂直策略) —— 一举两得
m_logView->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
```

**机制讲解**

`QSizePolicy` 有两个方向（Horizontal、Vertical），每个方向取一个策略值。常用取值：

| 策略 | 能缩（小于 sizeHint）？ | 能长（大于 sizeHint）？ | 想长？ |
|------|:---:|:---:|:---:|
| `Fixed` | 否 | 否 | 否（就 sizeHint 那么大） |
| `Minimum` | 否 | 是 | 中性 |
| `Maximum` | 是 | 否 | 否 |
| `Preferred` | 是 | 是 | 中性（给多少拿多少） |
| `Expanding` | 是 | 是 | **是（主动要额外空间）** |
| `MinimumExpanding` | 否 | 是 | **是** |
| `Ignored` | 是 | 是 | 是（连 sizeHint 都忽略，纯 stretch） |

读法：`Expanding` 的意思是"我**想要**多余空间"；`Preferred` 是"有多余给我也行，不给也行"。布局在分额外空间时，**优先照顾 `Expanding` 的控件**（把它当作"要分"），`Preferred` 的控件只在没人抢时才拿。

**为什么不能简单照搬**：WPF 里"Stretch 对齐 + 固定 Height"是两个独立属性；Qt 里一个 `QSizePolicy::Expanding` 同时表达了"我想横向长满、纵向长满"。C# 开发者容易只调 stretch 忘掉 sizePolicy，结果控件"stretch 给了但就是不长大"——因为它在体检表上写了"不想长"。

**在 DataScope 里的落地**：监控页的曲线容器、日志页的 `QPlainTextEdit` 都应设 `Expanding, Expanding`；按钮、标签保持 `Fixed` 或 `Preferred`（默认即可，别乱改）。**布局的大概分配用 stretch，控件自身的生长意愿用 sizePolicy，两者配合才是完整的弹性模型。**

### 3.3 setStretch / setStretchFactor —— 布局的"事后调整"

布局建好之后也能改 stretch，不用重建：

```cpp
// 方式一：按控件改（最常用）
layout->setStretchFactor(m_logView, 3);   // 日志区权重升到 3

// 方式二：按位置改（索引是 addWidget 的顺序 0,1,2,...）
layout->setStretch(1, 3);                  // 第 1 个 child 权重 = 3

// QGridLayout 的行/列权重：
grid->setRowStretch(0, 1);      // 第 0 行权重 1
grid->setColumnStretch(1, 2);   // 第 1 列权重 2
```

`setStretchFactor` 与 `addWidget(w, stretch)` 等价，只是**创建后改**。适合"根据运行状态调整布局"的场景（比如用户勾选了"展开日志区"）。注意 `setStretch(index, ...)` 的 index 是**布局内 child 的序号**，不是控件的什么东西——控件多了容易数错，**优先用 `setStretchFactor(widget, ...)`**。

### 3.4 为什么用布局替代绝对定位（Canvas → setGeometry 的对与错）

**代码对比**

```csharp
// WPF：Canvas 绝对定位
<Canvas>
    <Button Canvas.Left="120" Canvas.Top="40" Width="80" Height="30"/>
</Canvas>
```

```cpp
// Qt：绝对定位（对应物）
btn->setGeometry(120, 40, 80, 30);   // x, y, w, h —— 编译通过，但别再这么写
```

**机制讲解**

`setGeometry(x, y, w, h)` 给控件钉死坐标和尺寸，等价 WPF `Canvas`。它有以下致命伤：

1. **不随窗口缩放**。窗口拉大，按钮还在 `(120,40)` 尺寸不变，留下一片空白或重叠区。
2. **不随字体/DPI 变化**。Windows 缩放 150%、或系统字体变大时，按钮文字被裁、控件互相压。工业现场高分辨率/高缩放是常态，这是硬伤。
3. **手工算坐标不可维护**。加一个控件，要把周围所有坐标重算一遍——这就是 WPF 里没人用 Canvas 布整个界面的原因。

**什么时候 `setGeometry` 是合理的**：自绘控件内部（P14 的曲线绘制、自定义仪表），或在布局内的某个固定格子中精确定位子元素。**窗口级别的布局永远交给布局管理器**。

**DockPanel 的 Qt 对应**：Qt 的 `QDockWidget` 是"可浮动、可拖出主窗口"的停靠窗，语义更接近 WPF 的 `ToolWindow` 停靠，**不是** `DockPanel` 的"Top/Bottom/Left/Right 填边"。真要复刻 `DockPanel`（左停靠树、底部状态、中间填充），用**布局嵌套**：`QVBoxLayout`（外层：上工具栏 + 中间区 + 状态栏）里套 `QHBoxLayout`（中间区：左导航 + 右内容）。P5 的页签方案则直接用 `QTabWidget` 替代"中间内容区"——更简单也更贴工业软件的常见形态（见第 4 节）。

**在 DataScope 里的落地**：P4 的 `mainwindow.cpp` 已经展示了正确的雏形——`new QVBoxLayout(central)` + `addWidget(btnRun)` + `addWidget(m_logView)`。P5 要做的只是**把这套心智放大**：外层布局管"页签栏 + 状态栏"，页签内部每个页面再用自己的布局。

---

## 4. QTabWidget 详解：主窗口从"单面板"变"五页签"

> 一句话类比：`QTabWidget` 就是 WPF 的 `TabControl`——上面一排页签，下面一个 `QStackedWidget` 负责切换显示哪一页。你只管 `addTab`，**切换逻辑它全包了**。

### 4.1 addTab / setCurrentIndex / setCurrentWidget

```cpp
// P5 设计意图：主窗口中央区变成 QTabWidget，五个业务页面各占一个 tab
auto *tabs = new QTabWidget(central);          // 页签容器

auto *monitorPage = new MonitorPage(tabs);     // 页面 1：实时监控（P14 主角）
auto *channelPage = new ChannelConfigPage(tabs);// 页面 2：通道配置
auto *logPage     = new LogPage(tabs);          // 页面 3：日志（P6 LogManager 的 UI）
auto *settingsPage= new SettingsPage(tabs);     // 页面 4：设置（P7 ConfigManager 的 UI）
auto *aboutPage   = new AboutPage(tabs);        // 页面 5：关于

// 文字页签：addTab(widget, title) —— 返回该页的索引（从 0 开始）
int idxMonitor = tabs->addTab(monitorPage, tr("实时监控"));
tabs->addTab(channelPage, tr("通道配置"));
tabs->addTab(logPage,     tr("日志"));

// 图标 + 文字页签：addTab(widget, icon, title)
tabs->addTab(settingsPage,
             QIcon(QStringLiteral(":/icons/settings.png")), tr("设置"));
tabs->addTab(aboutPage, tr("关于"));

// 初始选中第 0 页；之后运行时切换：
tabs->setCurrentIndex(idxMonitor);      // 按索引切
tabs->setCurrentWidget(monitorPage);    // 按控件切（更不易错）

// 读取当前页：
int      curIdx  = tabs->currentIndex();      // 当前索引
QWidget *curPage = tabs->currentWidget();     // 当前页控件
```

**机制讲解**

- `addTab` 的返回值是页签索引，先存起来，`setCurrentIndex` 用它最稳——**别硬编码 0/1/2**，万一未来页签顺序调整，硬编码就指错页。
- `QTabWidget` 内部由 `QTabBar`（页签条）+ `QStackedWidget`（多页叠放，只显示当前页）组成。**所有 addTab 进去的页面都会同时存在**（对象树里都活着），只是非当前页不显示。所以页面的构造、定时器、连接都在 addTab 那一刻就建立了——这就是为什么 P14 监控页"一启动就在后台采集"也 OK。
- 切换时机：信号 `currentChanged(int index)`，想做"切到日志页时刷新"就 `connect(tabs, &QTabWidget::currentChanged, this, ...)`。

### 4.2 常用交互开关

```cpp
tabs->setTabsClosable(true);    // 页签显示关闭按钮（点了发出 tabCloseRequested 信号，需自己处理关闭）
tabs->setMovable(true);         // 页签可拖拽换序（WPF TabControl 没有，加分项）
tabs->setDocumentMode(true);    // 去掉 Windows 经典的大边框，更接近现代扁平
tabs->tabBar()->setExpanding(false);   // 页签不拉伸平分，各自按内容宽
tabs->tabBar()->setUsesScrollButtons(true);  // 页签多到溢出时出现滚动箭头
```

**注意**：`setTabsClosable(true)` 只是**显示关闭按钮**，点击后 Qt **不会自动删页面**——它发出 `tabCloseRequested(int)`，你要自己 `connect` 后 `widget(i)->deleteLater()`（对照指南 02 第 4 节：事件循环里删对象用 `deleteLater`）。工业软件主界面通常**固定五页**，不需要关闭，保持默认即可。

### 4.3 页签样式定制的 QSS 入口

QTabWidget 的样式由两个部件构成：`QTabWidget::pane`（内容边框面板）和 `QTabBar::tab`（每个页签）。深度定制在 7 节的主题 QSS 里给完整版，这里先给结构：

```css
/* 内容区边框（pane） */
QTabWidget::pane { border: 1px solid #2a2e37; top: -1px; }
/* 每个页签（tab） */
QTabBar::tab {
    background: #1e222b; color: #aab2bd;
    padding: 6px 18px; border: 1px solid transparent;
}
/* 选中态 —— 对应 WPF Trigger on IsSelected */
QTabBar::tab:selected { background: #2a2e37; color: #ffffff; }
/* 悬停态 */
QTabBar::tab:hover { background: #252a33; }
```

注意 `QTabBar::tab` 的选择器写法是**子部件（sub-control）**语法（双冒号），与"类选择器"（点）不是一回事——`::` 后跟子部件名，`:` 后跟伪状态，`#` 后跟 objectName，别混（见 5.1）。

---

## 5. QSS 语法深入：从"改颜色"到"写主题"

> 一句话类比：QSS 是 **CSS 的降级版 + 为 Qt 子部件扩展的方言**——选择器语法像 CSS，但没有 CSS 的动画/变量/布局能力；多了 `QTabBar::tab`、`QScrollBar::handle` 这种"子部件"选择器。

### 5.1 选择器体系（类型 / 类 / ID / 后代 / 子部件）

| 写法 | 名称 | 匹配规则 | WPF 对应 |
|------|------|----------|----------|
| `*` | 通配 | 所有控件 | — |
| `QPushButton` | 类型选择器 | `QPushButton` **及其所有子类** | `TargetType="{x:Type Button}"` |
| `.QPushButton` | 类选择器（带点） | **只匹配 `QPushButton` 本身**，不含子类 | 无直接对应 |
| `#objectName` | ID 选择器 | `objectName()` 等于该值的控件 | `x:Name` + 具体 Style |
| `QPushButton#okBtn` | 类型 + ID | 既是 QPushButton 又名为 okBtn | — |
| `QWidget QPushButton` | 后代选择器 | 所有祖先链上任一 `QWidget` 之下的按钮 | `ElementName`/相对查找 |
| `QFrame > QPlainTextEdit` | 子选择器 | **直接**父级是 QFrame 的文本框 | — |
| `QTabBar::tab` | 子部件选择器 | `QTabBar` 内部名为 `tab` 的子部件 | ControlTemplate 内部元素 |
| `QPlainTextEdit[readOnly="true"]` | 属性选择器 | 该属性值等于给定值 | `DataTrigger Binding` |

**类型选择器 vs 类选择器（最容易踩的坑）**：

```css
QFrame { border: 1px solid red; }     /* 会连 QPlainTextEdit 一起边框！因为它继承自 QFrame */
.QFrame { border: 1px solid red; }    /* 只框真正的 QFrame，QPlainTextEdit 不受影响 */
```

原理：`QPlainTextEdit` 的继承链是 `QAbstractScrollArea → QFrame → QWidget`。类型选择器 `QFrame` 匹配"是 QFrame 或它的子类"，所以文本框被误伤。**带点的 `.QFrame` 只匹配精确类型**。工业界面里大量控件继承 `QFrame`（`QLineEdit` 不继承 QFrame，但 `QTextEdit`/`QPlainTextEdit`/`QGroupBox`/`QFrame` 本体都继承），写 `QFrame` 类型选择器时**务必确认你是否真想连子类一起改**。

### 5.2 优先级（specificity）—— 谁说了算

QSS 的优先级规则**比 CSS 简化**，但"越具体越优先"的道理一样。实用排序：

```
#objectName（ID）        >  .ClassName（类）      >  类型选择器（QPushButton）
```

- 优先级**高**的规则覆盖**低**的；
- **同级**规则，**后写的覆盖先写的**；
- 属性选择器 `[readOnly="true"]` 的优先级**等价于类选择器**；
- 子部件选择器（`::tab`）不增加优先级等级，它只是"作用到内部子部件"。

```css
QPushButton { color: gray; }        /* 类型：最低 */
.QPushButton { color: red; }        /* 类：覆盖上面 */
#btnConnect { color: green; }       /* ID：覆盖上面两条 */
/* 最终 btnConnect 的文字是绿色 */
```

**为什么不能简单照搬 WPF**：WPF 里样式是按 `TargetType` 隐式套用 + 显式 `Style` 引用；QSS 里**同优先级靠文件里书写顺序**决定。你很容易遇到"我在后面明明写了 `#obj { background: blue }` 却不生效"——先查是不是有**更具体的规则**（比如 `QPushButton#obj` 比 `#obj` 更具体？不，`QPushButton#obj` 有类型+ID，比纯 `#obj` 更具体，会赢）把 `blue` 盖掉了。排查顺序：**先数选择器里含几个 ID/类，再数文件里谁在后**。

### 5.3 伪状态 `:hover / :checked / :disabled` vs WPF Trigger

**代码对比**

```csharp
// WPF：Style.Triggers 响应鼠标悬停
<Style TargetType="Button">
    <Setter Property="Background" Value="#3a3f4b"/>
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="Background" Value="#4a9eff"/>
        </Trigger>
        <Trigger Property="IsEnabled" Value="False">
            <Setter Property="Background" Value="#2a2e37"/>
        </Trigger>
    </Style.Triggers>
</Style>
```

```css
/* Qt：QSS 伪状态 —— 一个选择器一个规则 */
QPushButton { background: #3a3f4b; }
QPushButton:hover { background: #4a9eff; }      /* 鼠标悬停 */
QPushButton:pressed { background: #2f3a4a; }    /* 按下 */
QPushButton:disabled { background: #2a2e37; color: #5a6070; }  /* 禁用 */
```

**机制讲解**

QSS 伪状态（`:hover`/`:pressed`/`:checked`/`:disabled`/`:focus`/`:selected` 等）就是"状态 → 样式"的映射，**思想与 WPF `Trigger` 完全一致**，但有几个重要差异：

| WPF Trigger | QSS 伪状态 | 差异 |
|-------------|-----------|------|
| `IsMouseOver=True` | `:hover` | 名字不同 |
| `IsChecked=True` | `:checked`（按钮需 `setCheckable(true)`） | Qt 用"选中"而不是"勾选"命名 |
| `IsEnabled=False` | `:disabled` | `setEnabled(false)` 自动匹配 |
| `IsSelected`（ListBoxItem） | `:selected` | 列表/页签用 |
| `IsKeyboardFocusWithin` | `:focus` | 文本框聚焦用 |
| 无 | `:!selected`、`:!checked` | **支持取反**：`QTabBar::tab:!selected` 即"未选中的页签" |
| **可绑定到任意数据** | 只能绑定**控件内置状态** | **这是最大差别**：QSS 伪状态无法感知"采集线程传来的值"，只能在控件自身状态上响应 |

**为什么不能简单照搬**：WPF 的 `DataTrigger` 能把"`SensorValue > 100`"这种**业务数据**映射到样式；QSS **做不到**——它只看控件自己的状态机。工业界面的"数值超限变红"必须走代码：数据到来时 `widget->setProperty("alarm", true)` + 属性选择器 `QWidget[alarm="true"] { ... }`，或直接改 `setStyleSheet`/`QPalette`。**QSS 管"控件状态"，业务状态要用属性选择器或代码转发**（见 5.6 QSS 局限）。

### 5.4 border-radius：圆角从哪来

```css
QPushButton {
    border-radius: 4px;   /* 四角统一圆角 */
}
QLineEdit {
    border-radius: 6px;   /* 输入框更圆 */
}
```

Qt 的 `border-radius` 由 `QStyle` 绘制时裁切实现，与 WPF 的 `CornerRadius` 效果一致。几个细节：

- **只设一个值 = 四角同半径**；Qt 5.12 也支持 `border-top-left-radius` 等四个分开设置。
- **圆角必须配 border/background 才看得见**：透明背景 + 圆角没有意义，圆角裁的是**边框和背景**。
- **不是所有控件都完全支持**：`QComboBox` 的下拉弹层、`QMenu`、原生画法（如 Windows 风格的某些绘制路径）可能忽略圆角。遇到"圆角不生效"先怀疑子部件在原生画法里。

### 5.5 rgba()：半透明

```css
QWidget#overlay {
    background-color: rgba(0, 0, 0, 180);   /* 黑色 + 180/255 透明度 */
}
```

Qt 的 `rgba()` 第四参 alpha 范围是 **0–255**（不是 WPF 的 0–1，也不是 0–100%）。`rgba(0,0,0,180)` ≈ `#000000B4`（WPF 的 8 位十六进制 ARGB：`B4` = 180）。半透明在工业 UI 里常用于：模态遮罩层、报警弹窗背景、曲线网格线淡化。

**注意**：半透明背景只在**没有合成冲突**时有效。给一个普通 `QWidget` 设 `rgba` 背景，需要确保它没有不透明父级阻挡——通常在自绘/叠加层场景（P13/P14 状态机与自绘控件会用到）。

### 5.6 QSS 的局限：不是 CSS

把 QSS 当 CSS 用会撞四堵墙，心里有数就能绕开：

1. **不支持动画**。CSS 有 `transition`/`@keyframes`；QSS **完全没有**。悬停颜色是"啪"一下跳变。想要渐变过渡，只能代码里用 `QPropertyAnimation` 动画一个 `QPalette` 或自绘属性（P14 自绘曲线时再展开）。
2. **不支持绑定**。没有 `Binding`、没有 `DataContext`。业务数据 → 外观，必须走代码或属性选择器。
3. **不支持变量**。CSS 变量、嵌套、函数都没有。同一主题色要写很多遍，所以**主题色最好集中成常量**（`namespace` 里的 `const QString`），或 QSS 里用唯一值、代码里做字符串替换。
4. **全局字符串，错一个字符静默失效**。属性名拼错、颜色写错格式，QSS **不报编译错也不报运行错**——只是那条规则不生效。调试手段有限：`qApp->styleSheet()` 看当前样式表、逐条删减定位失效规则。
5. **`setStyleSheet` 是"重锤"**：每次调用都会触发全控件 `polish()`（重新解析并应用），频繁调用有性能代价。**主题切换时集中设一次**，不要在每秒的数据更新里逐控件 `setStyleSheet`。

---

## 6. 资源系统：resources.qrc

> 一句话类比：WPF 把 XAML/图片编成 `Resource`（松文件，随 exe 分发）；Qt 用 `rcc` 把资源**二进制直接嵌进 exe**——运行时没有"文件路径"这回事，只有一个虚拟路径。对应 WPF 的 pack URI，但比 pack URI 更"硬"。

### 6.1 resources.qrc 的 XML 结构

```xml
<?xml version="1.0" encoding="UTF-8"?>
<RCC>
    <!-- prefix="/" 是虚拟根路径；所有资源从它下面挂 -->
    <qresource prefix="/">
        <!-- 样式表：放在 resources/qss/main.qss -->
        <file>qss/main.qss</file>
        <!-- 图标：resources/icons/ 下 -->
        <file>icons/app.png</file>
        <file>icons/settings.png</file>
        <!-- alias 可以改名：下面这个用 /styles/dark.qss 访问，
             实际文件在 qss/dark-theme.qss -->
        <file alias="styles/dark.qss">qss/dark-theme.qss</file>
    </qresource>
</RCC>
```

关键点：

- `<qresource prefix="/">` 定义虚拟根；`<file>` 的路径是**相对 .qrc 文件位置**的磁盘路径。
- 默认（不写 alias）时，资源的虚拟路径 = `prefix + 文件路径`。上例 `qss/main.qss` 的访问路径是 `:/qss/main.qss`。
- **`resources/resources.qrc` 是共享文件，只有主 Agent 能写**——专项 Agent 需要新增资源时提交需求，由主 Agent 落笔（多 Agent 计划第三节）。

### 6.2 AUTORCC 编译流程

`DataScope/CMakeLists.txt` 第 24 行已经开了 `set(CMAKE_AUTORCC ON)`。构建时的流程：

```
resources/resources.qrc  --(rcc 工具)-->  qrc_resources.cpp  --(g++)-->  目标文件的 .obj
                          AUTORCC 自动完成                        资源二进制内嵌进 exe
```

- CMake 的 `AUTORCC` 会在构建前自动扫描 `add_executable`/`add_library` 里列出的 `.qrc` 文件，调用 Qt 自带的 `rcc` 生成一个 `qrc_resources.cpp`（内含压缩后的二进制数据表），再编译进程序。
- **资源在编译期就进 exe**——运行时 `QFile(":/qss/main.qss")` 读的是**内存里的字节**，不是磁盘文件。所以发布时**不需要**把 qss/图标拷到 exe 旁（与 WPF `Resource` 相似、与 `Content` 不同）。
- 想禁用某个资源的"内嵌"做运行时热更？Qt 有 `QFileSelector`、外部 QSS 加载等方案，但工业发布一般选"内嵌"——**行为确定、不怕被改坏**。

### 6.3 访问路径 `:/` 与 `qrc:/` vs WPF pack URI

| WPF | Qt | 说明 |
|-----|----|----|
| `pack://application:,,,/Styles/Main.xaml` | `qrc:/qss/main.qss` | 都能用虚拟路径访问"随程序分发"的资源 |
| `new Uri("pack://application:,,,/...")` | `QFile(":/qss/main.qss")` | Qt 里 `:/` 与 `qrc:/` 等价（`qrc:` 是正式 scheme，`:/` 是简写） |
| `ResourceDictionary Source="..."` | `<file>qss/main.qss</file>` + 读取 | 声明在哪、加载方式不同 |
| `Application.Current.Resources.MergedDictionaries.Add(...)` | `qApp->setStyleSheet(content)` | 全局应用样式 |

**为什么不能简单照搬**：WPF 的"资源路径"由程序集反射解析，路径写错会在**加载时抛异常**（你能发现）；Qt 的 `QFile(":/qss/main.qss")` 路径写错只是 `open()` 返回 `false`，**静默**。所以加载 QSS 的代码**必须检查 `open()` 返回值并打日志**（`LogManager`，P6 到位后用它）。

### 6.4 加载 QSS 的两种方式

```cpp
// 方式一：启动时一次性全局加载（推荐，主 Agent 在 main.cpp 集成）
QFile qss(QStringLiteral(":/qss/main.qss"));
if (qss.open(QIODevice::ReadOnly | QIODevice::Text)) {
    qApp->setStyleSheet(QString::fromUtf8(qss.readAll()));  // 全局生效
} else {
    qWarning() << "加载主样式失败：:/qss/main.qss";   // P6 起改用 LogManager
}

// 方式二：运行时局部覆盖（不推荐全局混用，易乱）
someWidget->setStyleSheet(QStringLiteral("background: #2a2e37;"));
```

**注意**：`qApp->setStyleSheet()` 是**全局样式表**，对整个 QApplication 生效；`widget->setStyleSheet()` 是**局部**，只作用该控件及其子树，且局部会**覆盖全局的同属性规则**（优先级高于全局）。工程约定：**主题走全局一张 QSS，个别"动态状态"用属性选择器或局部小段 QSS**，不铺满局部样式。

---

## 7. 关键代码片段：工业深色主题 QSS（逐段注释）

> 这一节是一份**可直接落地的深色主题 QSS**，覆盖 P5 主窗口的全部关键部件：页签栏、按钮、状态栏、输入框、只读日志区、滚动条、悬浮提示。文件建议放 `resources/qss/main.qss`（主 Agent 集成 `.qrc` 后按 `:/qss/main.qss` 加载）。

```css
/* ==========================================================================
 * main.qss —— DataScope Studio 工业深色主题（P5 设计意图稿）
 * 配色基调：深灰蓝底 + 蓝高亮 + 浅灰文字。写前先定"调色板"：
 *   背景1 #12141a（最深的日志区）  背景2 #1e222b（面板）  背景3 #2a2e37（抬起/选中）
 *   文字主 #c8ccd4                 文字弱 #7a8090          高亮   #4a9eff
 *   边框   #2a2e37
 * ========================================================================== */

/* ---- 0. 全局基调：字体、默认文字色、默认背景 ----
 * 类型选择器 QWidget 会作用到所有控件（含子类），所以这里只放"全局默认"，
 * 具体控件用更具体的规则覆盖。注意：这一条写在最前面，后面规则都会覆盖它。 */
QWidget {
    color: #c8ccd4;                /* 默认文字色：浅灰 */
    font-family: "Microsoft YaHei"; /* 工业 UI 中文优先雅黑 */
    font-size: 13px;
}

/* ---- 1. 主窗口：背景铺满 ---- */
QMainWindow {
    background-color: #1e222b;
}

/* ---- 2. 页签栏：pane（内容边框）+ tab（每个页签）----
 * 重点：QTabBar::tab:selected 去掉下边框，让选中页签与内容区"连成一体"；
 *      :!selected 取反 = 未选中页签；hover 悬停提示。 */
QTabWidget::pane {
    border: 1px solid #2a2e37;
    background-color: #1e222b;
    top: -1px;                     /* pane 上边内缩，压住页签底边 */
}
QTabBar::tab {
    background: transparent;       /* 默认透明，选中才"亮" */
    color: #7a8090;
    padding: 8px 20px;
    border: 1px solid transparent;
    border-top-left-radius: 4px;   /* 顶部两角微圆，贴合 pane */
    border-top-right-radius: 4px;
}
QTabBar::tab:hover {
    background-color: #252a33;     /* 悬停稍亮 */
    color: #c8ccd4;
}
QTabBar::tab:selected {
    background-color: #2a2e37;     /* 选中：抬起一块，与 pane 同底 */
    color: #ffffff;
    border-bottom: none;           /* 去掉底边，避免选中页签有"裂缝" */
}

/* ---- 3. 按钮：常态 / 悬停 / 按下 / 禁用 / 选中(切换型) ----
 * 对应 WPF Trigger 的 IsMouseOver/IsPressed/IsEnabled/IsChecked。 */
QPushButton {
    background-color: #2a2e37;
    color: #c8ccd4;
    border: 1px solid #3a3f4b;
    border-radius: 4px;
    padding: 6px 16px;             /* 文字到边框内边距（WPF Padding） */
    min-height: 16px;
}
QPushButton:hover {
    background-color: #353b48;     /* 悬停：稍亮 */
    border-color: #4a9eff;         /* 高亮边框，提示"可点" */
}
QPushButton:pressed {
    background-color: #2f3a4a;     /* 按下：压暗 + 蓝调 */
}
QPushButton:disabled {
    background-color: #1a1d24;     /* 禁用：沉下去，文字也变弱 */
    color: #5a6070;
    border-color: #262a33;
}
QPushButton:checked {
    /* 需要按钮 setCheckable(true) 才出现 :checked（切换型按钮）
       例如"暂停采集/恢复采集"这种两态按钮 */
    background-color: #4a9eff;
    color: #ffffff;
    border-color: #4a9eff;
}

/* ---- 4. 状态栏：与主窗口同底，顶部一条细边框隔开 ---- */
QStatusBar {
    background-color: #1e222b;
    color: #7a8090;
    border-top: 1px solid #2a2e37;
}
QStatusBar::item {
    border: none;                  /* 去掉 Qt 默认给 item 加的边框 */
}
QStatusBar QLabel {
    color: #7a8090;
    padding: 0 6px;
}

/* ---- 5. 输入框：常态描边 / 聚焦高亮 ----
 * QLineEdit 聚焦时把边框换成高亮蓝，让操作员一眼看到"当前焦点在哪"。 */
QLineEdit {
    background-color: #12141a;
    border: 1px solid #3a3f4b;
    border-radius: 4px;
    padding: 4px 8px;
    color: #c8ccd4;
    selection-background-color: #4a9eff;   /* 选中文字背景 */
    selection-color: #ffffff;              /* 选中文字前景 */
}
QLineEdit:focus {
    border-color: #4a9eff;         /* 聚焦高亮，比 hover 更醒目 */
}

/* ---- 6. 只读日志区：属性选择器定位 ----
 * 用 [readOnly="true"] 精确命中"日志/输出"这类只读文本框，
 * 比 #objectName 更"语义化"：凡是只读的都统一深底，改主题时不会漏。
 * 类型选择器 QPlainTextEdit 会命中所有纯文本编辑区，这里用属性选择器收窄。 */
QPlainTextEdit[readOnly="true"] {
    background-color: #12141a;     /* 最深一层：日志区要"退后" */
    color: #b8bcc6;
    border: 1px solid #2a2e37;
    border-radius: 2px;
    font-family: "Consolas";       /* 等宽字体，日志对齐更整齐 */
    font-size: 12px;
    selection-background-color: #2a3a4a;
}

/* ---- 7. 滚动条：细、圆角、颜色融入面板 ----
 * 工业 UI 滚动条不宜太粗；add-line/sub-line 设 0 高/宽，去掉两端箭头。 */
QScrollBar:vertical {
    background: transparent;
    width: 10px;
    margin: 0;
}
QScrollBar::handle:vertical {
    background: #3a3f4b;
    border-radius: 5px;
    min-height: 30px;
}
QScrollBar::handle:vertical:hover {
    background: #4a5060;
}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
    height: 0;                     /* 隐藏上下箭头 */
}
QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {
    background: transparent;       /* 轨道透明 */
}
/* 水平滚动条同理，略（width→height、vertical→horizontal） */

/* ---- 8. 悬浮提示 / 分组框 / 菜单（顺手补全）---- */
QToolTip {
    background-color: #2a2e37;
    color: #c8ccd4;
    border: 1px solid #3a3f4b;
    padding: 4px 8px;
}
QGroupBox {
    border: 1px solid #2a2e37;
    border-radius: 4px;
    margin-top: 10px;              /* 给标题留空间 */
}
QGroupBox::title {
    subcontrol-origin: margin;
    left: 10px;                    /* 标题内缩 */
    color: #7a8090;
}
QMenu {
    background-color: #1e222b;
    color: #c8ccd4;
    border: 1px solid #2a2e37;
}
QMenu::item:selected {
    background-color: #353b48;     /* 菜单悬停高亮 */
}
```

**逐段注释总结**：上面 9 段就是"从底到顶"的覆盖顺序——先全局默认（`QWidget`），再窗口/页签/按钮/输入框等具体控件，再到特殊形态（只读、滚动条、菜单）。**主题 QSS 是"层级覆盖"的艺术**：低优先级规则提供默认，高优先级规则按需覆盖，越往后越具体。

**配套加载代码**（主 Agent 集成到 `main.cpp`）：

```cpp
#include <QFile>
#include <QApplication>

// 在创建窗口之前加载全局主题（P5 设计意图）
QFile qss(QStringLiteral(":/qss/main.qss"));
if (qss.open(QIODevice::ReadOnly | QIODevice::Text)) {
    app.setStyleSheet(QString::fromUtf8(qss.readAll()));
} else {
    qWarning() << "加载主样式失败，界面将使用默认风格";  // P6 起改用 LogManager
}
```

---

## 8. 错误案例：把 C#/XAML 思维直接搬进 Qt 会踩的坑

### 错误案例 1：两个"可拉伸"控件直接 addWidget → 五五开挤压

**C#/XAML 惯性**：WPF 里两个 `*` 行就是精确 1:1，我想让日志区占大头，把"别的"行设小不就行了。

**Qt 照搬**：

```cpp
// 想当然：两个控件都放进去，觉得日志会"自动占满"
layout->addWidget(m_logView);   // QPlainTextEdit 默认 Expanding，想要大
layout->addWidget(btnRun);      // 按钮默认 Preferred
// 结果：日志区确实"想长大"，但布局把额外空间在"想长的控件"间摊派，
// 如果两个控件都想长（或 sizePolicy 都是 Expanding），就 50/50 平分——
// 你根本没表达"谁该多拿"。
```

- **现象**：窗口拉大时，日志区和另一个控件五五开，或者全挤在一起；你想让日志占 3/4，它只占 1/2。
- **根因**：stretch 默认都是 0，Qt 只看 **sizePolicy 里的"想长吗"** 来决定谁分额外空间；**你没用 stretch 表达比例**（3.1、3.2）。WPF 的 `*` 是精确比例，Qt 的 stretch 才是"分额外空间的权重"。
- **正确写法**：

```cpp
layout->addWidget(m_logView, 3);   // 日志区：额外空间权重 3
layout->addWidget(btnRun,    0);   // 按钮：只拿 sizeHint，不参与分额外空间
// 或者想按钮也占一点：layout->addWidget(btnRun, 1);
```

- **对照**：3.1 stretch vs Star sizing；3.2 sizePolicy。

### 错误案例 2：QSS 文件路径写错（相对路径 vs qrc:/）→ 样式静默不生效

**C#/XAML 惯性**：`ResourceDictionary Source="Styles/Main.xaml"` 是相对路径，编译器会解析成程序集资源，从不担心"当前目录"。

**Qt 照搬**：

```cpp
// 以为 QFile 相对路径像 C# 一样"从程序所在目录"解析
QFile qss(QStringLiteral("qss/main.qss"));   // 相对路径！
if (qss.open(QIODevice::ReadOnly)) {
    qApp->setStyleSheet(QString::fromUtf8(qss.readAll()));
}
// open() 返回 false → 样式一点没生效，界面还是默认灰白
```

- **现象**：程序跑起来是默认样式，主题完全没有加载；没有任何报错。
- **根因**：`QFile("qss/main.qss")` 是相对路径，按**当前工作目录（CWD）**解析。Windows 上从资源管理器双击 exe，CWD 可能是 `C:\Windows\System32` 或 exe 目录，**随启动方式漂移**（对照指南 03-01 的日志目录案例同款坑）；从命令行启动又是另一个目录。**"qss/main.qss 在不在 exe 旁"完全不可控。**
- **正确写法**：资源走 `.qrc` 编译进 exe，用虚拟路径 `:/qss/main.qss`——不依赖任何磁盘文件与 CWD：

```cpp
QFile qss(QStringLiteral(":/qss/main.qss"));   // qrc 虚拟路径，永远存在
if (qss.open(QIODevice::ReadOnly | QIODevice::Text)) {
    qApp->setStyleSheet(QString::fromUtf8(qss.readAll()));
} else {
    qWarning() << "加载主样式失败";   // P6 起改用 LogManager，绝不做"静默失败"
}
```

- **补充**：调试期真的想从磁盘读 QSS（改样式不用重新编译），可以加一个"仅调试"的绝对路径回退，但**发布版必须走 qrc**。判断"样式是否加载"：`qApp->styleSheet().isEmpty()`。
- **对照**：6.3 访问路径 vs pack URI；03-01 日志"配置文件找不到"是同一类 CWD 陷阱。

### 错误案例 3：`#objectName` 拼错 / objectName 没设 → 选择器失效

**C#/XAML 惯性**：WPF 里 `x:Name` 是强类型字段，写错名**编译器直接报错**，根本跑不起来。

**Qt 照搬**：

```cpp
// QSS 里写了：  #logView { background: #12141a; }
m_logView->setReadOnly(true);
m_logView->setMaximumBlockCount(500);
// 忘了写：m_logView->setObjectName(QStringLiteral("logView"));
// → #logView 匹配不到任何控件，这条规则静默失效！
```

- **现象**：日志区没变深色，别的样式都生效了，就这一个没生效。**没有编译错、没有运行错**。
- **根因**：QSS 的 `#objectName` 匹配的是 `QObject::setObjectName()` 设置的运行时名字。**objectName 不是成员字段**——默认是空字符串。WPF 的 `x:Name` 是编译器强类型成员（写错即编译失败）；Qt 的 objectName 是运行时字符串（写错静默）。
- **正确写法**：

```cpp
// ① 先设名字（在创建时紧挨着做，养成习惯）
m_logView->setObjectName(QStringLiteral("logView"));
// ② QSS 里名字拼写与之一字不差：
//    #logView { background: #12141a; }
// ③ 排查技巧：运行中断点看 qobject_cast<QWidget*>(...)->objectName()
//    或直接打印 m_logView->objectName()，对照 QSS 里写的字符串。
```

- **补充**：objectName 在调试里极有用——`QObject::findChild<QWidget*>("logView")` 能按名字找子控件，`dumpObjectTree()` 能打印整棵对象树看名字（WPF 没有直接对应物）。**给关键控件起规范 objectName 是 Qt 工程的好习惯**，它同时服务 QSS、调试、自动化测试三件事。
- **对照**：5.1 ID 选择器；5.2 优先级。

### 错误案例 4（补充）：子控件继承父控件 QSS → 样式污染

**C#/XAML 惯性**：`TargetType="{x:Type Button}"` 只命中 Button，绝不命中继承自 Button 的自定义控件（除非写 `BasedOn`）。

**Qt 照搬**：

```css
/* 想在"所有分组边框"上加个描边，于是写了类型选择器 */
QFrame { border: 1px solid #2a2e37; }
/* 结果：日志区 QPlainTextEdit 也是 QFrame 的子类 → 也被加上边框！
   QPlainTextEdit 继承链：QAbstractScrollArea → QFrame → QWidget */
```

- **现象**：只想给 QFrame 加边框，结果只读日志区、滚动区全被描了边；或某个通用 `QFrame` 的规则把整个页面都影响了。
- **根因**：**QSS 类型选择器匹配"该类及其所有子类"**。工业 UI 里 `QFrame` 子类满地都是（`QTextEdit`/`QPlainTextEdit`/`QGroupBox`/滚动区都是），写 `QFrame {}` 几乎等于"半个全局"。
- **正确写法**：用**类选择器（带点）**只匹配精确类型；或用 objectName 精确到单个控件：

```css
.QFrame { border: 1px solid #2a2e37; }   /* 只命中真正的 QFrame，子类不受影响 */
/* 或：#groupPanel { border: ... } 精确到 objectName */
```

- **对照**：5.1 类型选择器 vs 类选择器（最重要的一条差异）；5.2 优先级。

---

## 9. 练习题（请你亲自做）

> 以下题目都要**动手改代码 + 编译运行观察**，验证完在代码旁写一行中文注释记录你的观察。P5 阶段主 Agent 正在集成页签布局，改 `mainwindow.cpp` 有冲突风险——**改之前先备份或改完再还原**；若尚未完成页签集成，可在 P4 现有面板上验证布局/QSS 机制。

1. **stretch 观察实验**：在 `mainwindow.cpp` 的 `layout->addWidget(btnRun)` / `addWidget(m_logView)` 上分别加 `, 3` / `, 1`，运行后拉伸窗口，观察谁在变、谁纹丝不动；再改成 `, 0` 和 `, 1`，记录差异。回答：为什么"stretch=0 的控件"在窗口拉大时不长大？（提示：3.1 的"剩菜分配"）

2. **sizePolicy 实验**：把 `m_logView->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed)`，再拉窗口，观察日志区纵向不再长高。解释 `Expanding, Fixed` 与 `Expanding, Expanding` 的区别（提示：3.2 体检表）。

3. **给页面套滚动区**：新建一个测试页面 `QScrollArea`，`setWidgetResizable(true)`，放入一个固定尺寸的 `QWidget`（如 600×400 的 `QFrame`），作为 `QTabWidget` 的一页。把窗口缩小，观察滚动条出现；对比 `setWidgetResizable(true/false)` 的行为差异。这是工业配置页"内容放不下时滚动"的标准做法。

4. **改主题色**：把 `main.qss` 里的高亮色 `#4a9eff` 全局替换成 `#ff9800`（橙色），重新运行，列出所有"跟着变的控件"（按钮悬停、页签选中、输入框聚焦……）。体会"一套主题色牵动全身"——这正是把主题色集中成常量的理由。

5. **复现并修复错误案例 4**：在 `main.qss` 末尾临时加一条 `QFrame { border: 1px solid red; }`，运行看哪些控件被误伤（记录列表），再改成 `.QFrame { ... }` 对比。写一段注释解释两者差异。

6. **QTabWidget 页签定制**：实现一个"分段式页签"——选中页签用 `border-bottom: none` 与内容区连成一体（仿 7 节第 2 段），未选中页签透明；再试试 `setDocumentMode(true)` 前后的视觉差异。

7. **属性选择器实验**：给日志区 `QPlainTextEdit` 写 `[readOnly="true"]` 规则，验证生效；再把 `setReadOnly(true)` 注释掉，观察规则是否失效（理解"属性选择器匹配的是运行时属性值"）。

8. **资源路径实验（需主 Agent 集成 qrc 后做）**：验证 `QFile(":/qss/main.qss")` 能打开；故意写错路径 `QFile(":/qss/main1.qss")`，观察 `open()` 返回 `false` 且不抛异常——理解"Qt 资源错误是静默的"，养成检查返回值 + 打日志的习惯。

---

## 10. 阶段验收 checklist（P5 布局与 QSS）

> 验收以**可运行 + 可观测**为准，逐项打勾。符合后由主 Agent 集成进 `CMakeLists.txt`、`resources/resources.qrc` 并更新 `stages/P05.md`。

**主窗口页签布局**

- [ ] 主窗口中央区已由 P4 单面板重构为 `QTabWidget`，五个页面（监控/通道配置/日志/设置/关于）各占一个页签。
- [ ] 页面类位于 `src/ui/pages/`（如 `monitorpage.h/.cpp`），每个页面类继承 `QWidget`，构造函数内自建自己的布局。
- [ ] `addTab` 返回值（索引）被保存并用于 `setCurrentIndex`，**无硬编码页签索引**。
- [ ] 页面内控件一律用布局管理器（`QVBoxLayout`/`QHBoxLayout`/`QGridLayout`），**无窗口级 `setGeometry` 绝对定位**。
- [ ] 弹性分配合理：主要区域（如监控曲线区）stretch 大、辅助区域 stretch 小；`QPlainTextEdit`/曲线容器 `sizePolicy` 为 `Expanding`。
- [ ] 关键控件设置了有意义的 `objectName`（服务 QSS、调试、测试）。

**QSS 主题**

- [ ] `resources/qss/main.qss` 深色主题已建立（主 Agent 集成 `.qrc`），通过 `:/qss/main.qss` 在 `main.cpp` 启动时全局加载。
- [ ] 覆盖部件至少包括：页签栏（pane/tab/selected/hover）、按钮（hover/pressed/disabled/checked）、状态栏、输入框（focus）、只读日志区、滚动条。
- [ ] 加载代码检查 `open()` 返回值，失败时打日志（P6 起走 `LogManager`），**不做静默失败**。
- [ ] 全工程无"局部 `setStyleSheet` 满天飞"；动态状态优先用属性选择器或代码，而非逐控件设局部样式。

**规范与集成**

- [ ] 未出现"局部局部覆盖全局"混乱；`resources/resources.qrc` 仅主 Agent 修改，专项 Agent 只提交资源文件需求。
- [ ] `cmake --build build` 一键构建通过；`DataScope.exe` 启动后深色主题生效、五页签可切换、窗口缩放时布局自适应。
- [ ] 教学文档 `docs/03-Qt学习地图/02-Qt界面布局与QSS样式.md` 与代码对应（本文件为设计意图稿，主 Agent 集成后回填最终行号）。

---

## 附录 A：本文与工程文档的关系

| 参考来源 | 用途 |
|----------|------|
| `docs/04-WPF转Qt对照指南/01-C++与Qt核心概念对照.md` | 信号槽、事件循环、值语义等前置概念（本文引用其章节） |
| `docs/04-WPF转Qt对照指南/02-Qt对象模型与内存管理.md` | 对象树、`deleteLater`、QObject 线程亲和（QTabWidget 页面的生命周期/删除时机锚点） |
| `docs/03-Qt学习地图/01-Qt基础设施-日志与配置管理.md` | "设计意图稿"表述、五段式结构、错误案例/练习/checklist 写法（本篇承继） |
| `docs/01-项目规划文档/02-多Agent协同开发计划.md` | P5 并行矩阵（`U2` UI / `D3` 教学）、共享文件隔离（`resources.qrc` 仅主 Agent 可写） |
| `DataScope/CMakeLists.txt` | `AUTOMOC/AUTORCC/AUTOUIC` 三开关、`find_package(Qt5 ... Widgets)`、共享文件登记（本文 §6.2 依据） |
| `DataScope/src/ui/mainwindow.cpp` | P4 真实代码：`QVBoxLayout`（第 69 行）、`addWidget`（第 81-82 行）、`statusBar()`（第 54 行）、只读日志区（第 78-79 行）——本文的"真实锚点" |
| `DataScope/src/ui/pages/monitorpage.h`（P5 集成后） | 五页面之一的教学示例，落地后作为 3/4 节页签与布局代码的真实对应 |

> 后续：`docs/03-Qt学习地图/` 将随阶段补充「事件循环与线程」「模型/视图」「网络与协议」「自绘控件与曲线」「调试与构建」等分册。本文的 QSS 局限（无动画/无绑定）将在 P13/P14 自绘控件阶段用 `QPropertyAnimation` 与属性选择器补全。
