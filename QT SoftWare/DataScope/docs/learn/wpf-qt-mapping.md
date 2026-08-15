# WPF → Qt 深入映射（docs/learn 核心）

> **读者**：从 WPF/C# 转学 Qt 的工程师。
> **写法**：每个概念五段式 —— **WPF 机制 → Qt 对应 → 本质区别 → 为什么不能一一对应 → 本项目实际代码**。
> 不追求"一一对应"的翻译式对照，而是讲清两个框架的**思维差异**：WPF 重声明式（XAML + 绑定引擎），Qt 重命令式（C++ 对象 + 信号槽）。

---

## 目录

1. [总览表：14 个核心概念速查](#1-总览表)
2. [XAML / 资源系统 → QSS / 资源系统](#2-xaml--资源系统--qss--资源系统)
3. [DependencyProperty → Q_PROPERTY](#3-dependencyproperty--q_property)
4. [INotifyPropertyChanged → 信号 + dataChanged](#4-inotifypropertychanged--信号--datachanged)
5. [ObservableCollection → QAbstractListModel / QAbstractTableModel](#5-observablecollection--qabstractlistmodel--qabstracttablemodel)
6. [Binding / DataTemplate → Model/View + role + delegate](#6-binding--datatemplate--modelview--role--delegate)
7. [Dispatcher / 线程亲和 → 事件循环 / moveToThread](#7-dispatcher--线程亲和--事件循环--movetothread)
8. [async/await → 信号槽异步 + Worker](#8-asyncawait--信号槽异步--worker)
9. [BackgroundWorker → QThread + Worker](#9-backgroundworker--qthread--worker)
10. [路由事件 RoutedEvent → 信号槽（五种连接）](#10-路由事件-routedevent--信号槽五种连接)
11. [ICommand → QAction](#11-icommand--qaction)
12. [Grid / StackPanel → 布局管理器](#12-grid--stackpanel--布局管理器)
13. [样式触发器 → QSS 选择器](#13-样式触发器--qss-选择器)
14. [绑定自动刷新 → 手动 connect + emit（本项目数据总线案例）](#14-绑定自动刷新--手动-connect--emit)
15. [总结：从 WPF 思维迁移到 Qt 思维](#15-总结)

---

## 1. 总览表

| # | WPF 机制 | Qt 对应 | 一句话本质区别 |
|---|----------|---------|----------------|
| 1 | XAML + App.xaml | QSS + 资源系统 | XAML 是声明式对象树，QSS 只是样式表；无 DataTemplate |
| 2 | DependencyProperty | Q_PROPERTY | DP 带完整属性系统（继承/变更通知），Q_PROPERTY 是宏声明 + 手动信号 |
| 3 | INotifyPropertyChanged | 信号 + dataChanged | WPF 绑定引擎自动订阅，Qt 需手动 connect + emit |
| 4 | ObservableCollection | QAbstractListModel | WPF 集合绑定自动同步，Qt 需实现 rowCount/data 并手动 begin/end |
| 5 | Binding + DataTemplate | Model/View + role + delegate | WPF 任意对象模板化，Qt 靠 role 取数、委托绘制 |
| 6 | Dispatcher | 事件循环 + 线程亲和 | WPF Dispatcher.Invoke，Qt 队列连接 + moveToThread |
| 7 | async/await | 信号槽异步 + Worker | C# 编译器状态机，Qt 事件驱动回调 |
| 8 | BackgroundWorker | QThread + Worker | 语义相近：后台 + 进度 + 完成 |
| 9 | RoutedEvent | 信号槽 | 路由事件冒泡/隧道，Qt 直连广播 |
| 10 | ICommand | QAction | Command 带 CanExecute 绑定，Qt 动作 + setEnabled 手动 |
| 11 | Grid/StackPanel | 布局管理器 | WPF 坐标+面板混合，Qt 布局类自动算几何 |
| 12 | 样式触发器 | QSS 选择器 + 属性 | WPF 更细粒度，Qt QSS 能力有限 |
| 13 | 绑定自动刷新 | 手动 connect + emit | WPF 声明式，Qt 命令式接线 |
| 14 | 事件聚合（Prism） | 全局单例信号 | DataService 即本项目的事件聚合总线 |

---

## 2. XAML / 资源系统 → QSS / 资源系统

**WPF**：`App.xaml` 定义全局资源字典（样式/模板/颜色），XAML 声明式构建整个 UI 对象树。

**Qt**：`QSS`（类似 CSS 的样式表）通过 `QWidget::setStyleSheet()` 应用；`resources/qss/main.qss` 编译进 `qrc` 资源。

**本质区别**：
- WPF 的 XAML 是**语言**——可以声明任意对象、数据模板、行为；QSS 只是**字符串样式表**，只改外观不改结构；
- QSS 选择器靠对象树属性匹配（`#objectName`、`QPushButton`），没有 WPF 的 DataTemplate/Style/Template 绑定。

**为什么不能一一对应**：Qt 没有声明式 UI 语言，控件树必须在 C++（或 QML）里命令式创建。QSS 只能"化妆"，不能"换器官"。这决定了 Qt Widgets 的学习重点是布局 + 信号槽而非模板系统。

**本项目代码**：
- 主题加载：`MainWindow::applyStyleSheet()`（`src/ui/mainwindow.cpp`）读 `:/qss/main.qss`；
- 对象定位：控件 `setObjectName("channelCard")`，QSS 里 `QFrame#channelCard { ... }` 按名字命中（`src/ui/pages/monitorpage.cpp`）；
- 主题切换：`SettingsPage::themeChanged` 信号 → `MainWindow::onThemeChanged` 加载/清空 QSS（对应 WPF 动态换 ResourceDictionary）。

---

## 3. DependencyProperty → Q_PROPERTY

**WPF**：`DependencyProperty` 是完整属性系统——值继承、变更通知、动画、绑定目标，全由 WPF 引擎托管。

**Qt**：`Q_PROPERTY(type name READ ... WRITE ... NOTIFY ...)` 宏声明属性，供元对象系统、QSS、QVariant 访问。

**本质区别**：
- DP 的变更通知由 WPF 绑定引擎**自动**传播；Q_PROPERTY 只是**注册表**，实际通知要靠 NOTIFY 信号手动 emit；
- DP 有"属性值优先级"（本地值 > 样式 > 默认），Qt 无此机制。

**为什么不能一一对应**：Q_PROPERTY 是给元对象系统（反射）用的元数据，不是给绑定引擎用的依赖属性。Qt Widgets 的数据流动**主要靠信号槽，不是属性绑定**。

**本项目代码**：控件用 `Q_PROPERTY` 暴露可样式化属性（如 `LedIndicator` 的 `color`），供 QSS 和外部设置；数据变化用信号（`dataUpdated`）而不是属性绑定。

---

## 4. INotifyPropertyChanged → 信号 + dataChanged

**WPF**：ViewModel 实现 `INotifyPropertyChanged`，setter 里 `OnPropertyChanged(name)`，绑定引擎自动更新 UI。

**Qt**：Model 修改数据后手动 `emit dataChanged(index, index)`；业务对象发业务信号（如 `dataUpdated`）。

**本质区别**：
- WPF 是**基于属性名的自动绑定**：`PropertyChanged("Name")` 后任何绑定到 Name 的控件都刷新；
- Qt 是**显式接线**：谁关心变化谁自己 connect 信号。没有"魔法"，每条数据通路都看得见。

**为什么不能一一对应**：WPF 把"数据 → UI"的同步约定成了框架默认；Qt 把它**还给了程序员**。代价是样板代码，收益是**数据流完全可追踪**（调试友好）。

**本项目代码**：
- 跨线程数据分发：`AcquisitionWorker::pointsReady` → `DataService::dataUpdated` → `MonitorPage::onDataUpdated`（`src/services/dataservice.cpp`、`src/ui/pages/monitorpage.cpp`）；
- Model 内部通知：`DeviceListModel::setDeviceState` 修改后 emit `dataChanged`（`src/ui/models/devicelistmodel.cpp`）。

---

## 5. ObservableCollection → QAbstractListModel / QAbstractTableModel

**WPF**：`ObservableCollection<T>` 实现 `INotifyCollectionChanged`，`Add` 后列表自动刷新。

**Qt**：`QAbstractListModel` / `QAbstractTableModel` 是**接口类**——必须实现 `rowCount()` 和 `data(index, role)`；插入数据前调 `beginInsertRows()`、插入后调 `endInsertRows()` 触发 View 刷新。

**本质区别**：
- WPF 的集合是"**现成容器 + 自动通知**"；Qt 的 Model 是"**你需要自己写容器 + 手动通知时机**"；
- `data()` 按 **role** 返回数据（DisplayRole 显示文本、TextAlignmentRole 对齐…），View 通过 role 取数据，Model 与 View 彻底解耦。

**为什么不能一一对应**：Qt 把 Model 做成了可继承的接口而非现成类，因为它要给**任意数据结构**提供适配（文件树、数据库结果、自绘网格…）。代价是样板，收益是 Model 与 View 零耦合——同一个 Model 可以接 QListView/QTableView/组合框。

**本项目代码**：
- `DeviceListModel : QAbstractTableModel`（`src/ui/models/devicelistmodel.h/cpp`）：设备表，`rowCount`/`columnCount`/`data` + `setDeviceState` 触发 `dataChanged`；
- `AlarmEventModel : QAbstractListModel`（`src/ui/models/alarmeventmodel.h/cpp`）：报警列表，`appendEvent` 用 `beginInsertRows/endInsertRows` 头插；
- View 端：`QTableView`（设备页 `DevicePage::buildDeviceTable`）与 `QListView`（监控页报警中心 `MonitorPage::buildAlarmCenter`）**只 setModel**，不感知数据来源。

---

## 6. Binding / DataTemplate → Model/View + role + delegate

**WPF**：`Binding` 把属性绑到数据源，`DataTemplate` 把对象模板化成任意控件树。

**Qt**：View 的每行通过 `data(index, role)` 取显示内容；想自定义每行外观用 `QStyledItemDelegate`（命令式绘制）或自定义控件（本项目做法）。

**本质区别**：
- WPF 的模板是**声明式 XAML**，可以给一个对象定义完整的控件树；
- Qt 的 delegate 是**命令式 paint()**，在画布上自己画（文本/进度条/颜色）。

**为什么不能一一对应**：Qt Widgets 的 item view 体系（QListView 等）是"**轻量高性能的行列渲染器**"，不是 WPF 的"内容呈现器"。当一行数据需要丰富交互时，Qt 的惯例是**放弃 item view，直接用布局管理器拼自定义控件**——本项目的通道卡片就是这条路。

**本项目代码**：
- 简单行数据：`QListView` + `AlarmEventModel`（`MonitorPage::buildAlarmCenter`）——`data()` 拼文本即可；
- 富交互卡片：**不用 item view**，用 `QVBoxLayout + QHBoxLayout` 拼 `LineChartWidget/GaugeWidget/LedIndicator`（`MonitorPage::buildChannelCard`）——这就是"WPF DataTemplate"的 Qt Widgets 替代方案。

---

## 7. Dispatcher / 线程亲和 → 事件循环 / moveToThread

**WPF**：UI 只能由 UI 线程操作，其他线程必须 `Dispatcher.Invoke(() => ...)` 把工作编回 UI 线程。

**Qt**：每个 `QThread` 有自己的事件循环；对象通过 `moveToThread()` 与线程绑定（线程亲和）。**信号槽跨线程自动排队**（QueuedConnection），槽在接收者线程执行，无需手动 marshal。

**本质区别**：
- WPF 要你**显式**用 Dispatcher 回主线程；Qt 用**队列连接自动**做这件事——发射信号时若发送者/接收者不同线程，Qt 把调用打包投递到接收者的事件循环；
- 但这要求**对象必须在它所属的线程创建**（socket 等），这是 Qt 的"铁律"：不能在主线程 new 再 moveToThread。

**为什么不能一一对应**：Qt 把线程亲和**编进对象模型**（每个 QObject 有 thread()），而 WPF 是"线程 + Dispatcher"两个独立概念。理解"对象属于哪个线程，它的槽就在哪个线程跑"是 Qt 并发的基础。

**本项目代码**（P12 采集链路）：
- `DataService` 在主线程持有 `QThread` + `AcquisitionWorker`（`moveToThread`），Worker 的 `start()/stop()` 槽在采集线程执行；
- `TcpClient` 在 `Worker::start()` 槽内创建（`src/services/acquisitionworker.cpp`）——**socket 在采集线程出生**；
- Worker 发 `pointsReady(QVector<DataPoint>)` 信号，队列投递回主线程的 `DataService::onPointsReady`——UI 侧零跨线程代码。

---

## 8. async/await → 信号槽异步 + Worker

**WPF/C#**：`async` 方法里 `await` 让编译器生成状态机，把异步代码写成同步样式。

**Qt**：没有语言级异步；异步 = 发起操作 + 订阅结果信号。`connectTo` 返回 void，结果由 `connected`/`errorOccurred` 信号通知。

**本质区别**：
- C# 的 async/await 把"**异步流程**"藏在编译器里；Qt 把"**异步的每一跳**"摆在明面上（发起 → 信号 → 再发起 → 再信号）；
- Qt 的异步代码因此**没有调用栈连续性**——每个回调是独立函数，共享状态要自己管理。

**为什么不能一一对应**：Qt 信号槽模型诞生时没有语言级协程。现代 Qt 有 `std::async`/协程支持，但生态惯例仍是信号槽。**迁移心态**：从"写同步代码让编译器拆"改为"按事件拆函数"。

**本项目代码**：`MainWindow::connectDevice`（`src/ui/mainwindow.cpp`）：
```cpp
m_service->connectTo(host, port);   // 异步发起，立即返回
m_service->startAcquisition();
// 结果：onServiceConnected() / onServiceError(msg) 由信号驱动
```
没有 await，只有"connectTo → 等信号 → onServiceConnected"。

---

## 9. BackgroundWorker → QThread + Worker

**WPF**：`BackgroundWorker` 封装后台线程：`DoWork` 干重活、`ProgressChanged` 报进度、`RunWorkerCompleted` 收尾，全部事件式。

**Qt**：`QThread + Worker（moveToThread）+ 信号` 是最正统的同类方案。

**本质区别**：语义几乎一一对应（后台执行 + 进度 + 完成回调），差别在样板：Qt 需要自己创建 QThread、moveToThread、管理生命周期、在析构时 quit+wait。

**为什么能较好对应**：BackgroundWorker 本身就是"事件驱动的后台线程封装"，和 Qt 线程模型天然同构。这是 WPF 程序员最容易迁移的模式。

**本项目代码**（P12）：`DataService`（`src/services/dataservice.h/cpp`）：
- `m_thread` = 后台线程（QThread）；
- `m_worker` = AcquisitionWorker（DoWork 等价物，moveToThread 后信号槽驱动）；
- `pointsReady/dataUpdated` = RunWorkerCompleted 携带结果；
- 析构 `stopAcquisition → quit → wait(3000)` = 安全收尾。

---

## 10. 路由事件 RoutedEvent → 信号槽（五种连接）

**WPF**：`RoutedEvent` 沿可视化树**冒泡/隧道**传播（Button.Click 冒泡到父容器）。

**Qt**：信号槽是**点对点广播**——发射信号的 `emit` 直接调用所有已连接槽（同步，同线程时）。没有路由树，没有冒泡/隧道。

**本质区别**：
- WPF 路由事件是"**从叶向根找处理器**"；Qt 信号槽是"**发射者 → 订阅者集合**"，谁订阅谁收到，与父子关系无关；
- Qt 信号是 **public 成员函数**，可以直接调用（触发所有槽），这暴露了"信号 = 广播方法"的本质。

**为什么不能一一对应**：Qt Widgets 的事件体系（`keyPressEvent`/`mousePressEvent` 虚函数）更接近路由事件，但那是**系统事件**；而**业务数据流动**全部用信号槽。把"WPF 里用事件做的"换成"信号槽做的"就是迁移核心。

**本项目代码**：`SignalSlotDemo`（`src/app/signalslotdemo.cpp`，P4）演示五种连接：直接连接、函数指针、lambda、对象方法、断开连接句柄。例如 `emit demo.counterChanged(42)` 是直接调用信号（公开函数），所有已连接槽同步执行。

---

## 11. ICommand → QAction

**WPF**：`ICommand`（RelayCommand）封装"执行 + 可执行判断"，`CanExecute` 自动刷新按钮可用态。

**Qt**：`QAction` 是一个"动作对象"——文本、图标、快捷键、`setEnabled`，`triggered` 信号。

**本质区别**：
- WPF Command 的 CanExecute 是**绑定到方法/属性**，可执行态随状态**自动**评估；
- Qt 的 `setEnabled(bool)` 是**手动**设置——你要在状态变化处自己调 `m_actStart->setEnabled(true/false)`。

**为什么不能一一对应**：Qt 没有命令绑定引擎，动作状态是命令式管理的。本项目用状态同步函数（`onServiceConnected`/`onServiceDisconnected`）集中管理一组 QAction 的可用态，等价于手写 CanExecute。

**本项目代码**（`src/ui/mainwindow.cpp`）：
- 工具栏 4 个 `QAction`：连接/断开/启动/停止；
- `onServiceConnected()` 里统一 `m_actStart->setEnabled(true)`、`m_actStop->setEnabled(true)` ——这就是"手动 CanExecute"；
- 快捷键：`quitAction->setShortcut(QKeySequence::Quit)`（等价 WPF InputBinding）。

---

## 12. Grid / StackPanel → 布局管理器

**WPF**：`Grid`（行列定义 + 绝对跨度）、`StackPanel`（顺序堆叠）、`DockPanel`（停靠）。布局是**XAML 属性 + 坐标混合**。

**Qt**：`QGridLayout`（行列网格）、`QVBoxLayout/QHBoxLayout`（垂直/水平堆叠）、`QFormLayout`。布局是**C++ 对象**，自动算几何；控件加入布局后由布局管理大小。

**本质区别**：
- WPF 布局是**声明式 + 自由定位**；Qt 布局是**命令式 + 强制结构**——控件要么在布局里（被布局管），要么 setGeometry（手动，不推荐）；
- Qt 的 `stretch`（拉伸因子）≈ WPF 的 `*` 尺寸；`addStretch` ≈ 弹性空隙。

**为什么不能一一对应**：WPF 可以自由 Mixed（Panel 里放 Absolute），Qt 更"规矩"——几乎一切靠布局嵌套。**思维迁移**：先画嵌套结构（哪层水平、哪层垂直），再翻译成布局对象。

**本项目代码**（`MonitorPage::buildChannelCard`，`src/ui/pages/monitorpage.cpp`）：
```cpp
auto *row = new QHBoxLayout(card);              // 卡片 = 横向
row->addLayout(infoCol);                        // 左：信息列（纵向）
row->addWidget(chart, /*stretch=*/1);           // 中：曲线，拉伸占剩余
row->addWidget(gauge);                          // 右：仪表，固定宽
```
整体：`QVBoxLayout`（标题 + 通道卡片区 + 报警中心）嵌套每卡片 `QHBoxLayout`——正是 Grid/StackPanel 组合的 Qt 版。

---

## 13. 样式触发器 → QSS 选择器

**WPF**：`Style.Triggers` 按属性值/数据变化切换样式（鼠标悬停变蓝）。

**Qt**：QSS 支持 `:hover`、`:checked`、`:disabled` 等**伪状态**选择器；复杂状态切换要代码里改 objectName 或属性 + 重设 QSS。

**本质区别**：WPF 触发器可基于**任意绑定值**（数据驱动样式）；Qt QSS 伪状态是**内建的 UI 状态**（悬停/禁用/选中），不能绑业务数据。

**为什么不能一一对应**：QSS 是 CSS 家族——它只懂 UI 状态，不懂业务数据。数据驱动的视觉变化（值超限变红）必须用**代码改属性/样式**，本项目就是 `led->setColor(...)` 手动切色。

**本项目代码**（`MonitorPage::onDataUpdated`，`src/ui/pages/monitorpage.cpp`）：
```cpp
if (dp.value > alarm) ui.led->setColor(QColor(0xe7,0x4c,0x3c)); // 超限红
else                  ui.led->setColor(QColor(0x16,0xa0,0x85)); // 正常绿
```
这就是"数据触发样式变化"的 Qt 命令式写法（WPF 触发器做不到业务绑定，Qt 用代码做到）。

---

## 14. 绑定自动刷新 → 手动 connect + emit（本项目数据总线案例）

**WPF**：三处绑定（界面 XAML、集合、命令）自动同步，数据流对开发者半透明。

**Qt**：所有数据流都是**显式 connect**。本项目的"数据总线"（DataService）就是**手写事件聚合器**（≈ Prism EventAggregator）。

**本质区别**：WPF 靠框架自动装配，**写起来少、追踪难**；Qt 手动装配，**写起来多、追踪易**——每条通路 connect 一目了然。

**本项目代码**（`MainWindow` 构造，`src/ui/mainwindow.cpp`）——一条数据流的完整接线：
```cpp
// 1. 采集线程产数据 → 主线程总线
//    Worker::pointsReady ──→ DataService::onPointsReady ──→ dataUpdated
// 2. 总线 → 两个消费者（监控页 + 记录器）
connect(m_service, &DataService::dataUpdated, m_monitorPage,   &MonitorPage::onDataUpdated);
connect(m_service, &DataService::dataUpdated, m_recordManager, &RecordManager::appendData);
// 3. 回放数据也走同一入口（数据源可替换：录制的帧重放进同一通道）
connect(m_recordManager, &RecordManager::replayData, m_monitorPage, &MonitorPage::onDataUpdated);
```
读这段代码就能看到**整条数据流**——这正是 Qt 式"显式 > 隐式"的工程收益。

---

## 15. 总结：从 WPF 思维迁移到 Qt 思维

| WPF 思维 | Qt 思维 |
|----------|---------|
| "我声明属性，框架帮我同步 UI" | "我发信号，谁关心谁 connect" |
| "我写 XAML，框架建对象树" | "我写 C++，用布局拼对象树" |
| "我 await，编译器拆状态机" | "我按事件拆函数，信号串起流程" |
| "我绑 ObservableCollection，自动刷列表" | "我实现 QAbstractListModel，手动 begin/end" |
| "UI 线程用 Dispatcher 编回" | "对象属于它的线程，队列连接自动投递" |
| "命令自动 CanExecute" | "状态变化处手动 setEnabled" |

**一句话**：WPF 把数据流**声明成框架约定**，Qt 把数据流**写进源码里**。学 Qt 不是学新语法，是接受"每条通路都要你亲手连"——而这恰是 Debug 工业软件时最珍贵的能力。

---

*下一篇：`architecture-walkthrough.md`（架构导览/源码行走）——用一条数据流把本项目的每一层走一遍。*
