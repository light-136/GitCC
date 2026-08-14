# WPF 开发者视角的 Qt 学习指南

> 面向：熟悉 WPF/C#、初次系统学习 Qt/C++ 的开发者
> 配套项目：GD32FirmwareUpdaterQt（本仓库）
> **学习方法**：每个概念先给出 WPF 等价物做"锚点"，再讲 Qt 原理与差异，最后落到本项目代码。

---

## 目录（12 大专题）

- [专题1 工程体系与构建](#专题1-工程体系与构建)
- [专题2 QObject 与元对象系统](#专题2-qobject-与元对象系统)
- [专题3 信号与槽](#专题3-信号与槽)
- [专题4 事件循环与事件处理](#专题4-事件循环与事件处理)
- [专题5 对象树与内存管理](#专题5-对象树与内存管理)
- [专题6 核心类型与容器](#专题6-核心类型与容器)
- [专题7 Widgets 控件与布局](#专题7-widgets-控件与布局)
- [专题8 QSS 样式表](#专题8-qss-样式表)
- [专题9 Model/View 架构](#专题9-modelview-架构)
- [专题10 线程与并发](#专题10-线程与并发)
- [专题11 IO 与串口通讯](#专题11-io-与串口通讯)
- [专题12 调试、国际化与部署](#专题12-调试国际化与部署)

---

## 专题1 工程体系与构建

### WPF 锚点
`.csproj` / `.sln` / MSBuild / NuGet / `dotnet build`

### Qt 对应物

| WPF 概念 | Qt 概念 |
|----------|---------|
| .csproj + .sln | `.pro`（qmake 工程文件） |
| MSBuild | qmake → 生成 Makefile → 编译器构建 |
| `<PackageReference>` NuGet | `QT += widgets serialport`（编译期选模块） |
| Visual Studio 解决方案 | Qt Creator（也可用 VS + Qt VS Tools） |
| C# 编译管线 | **MOC 预编译器**（Qt 独有） |

### 核心原理：MOC（Meta-Object Compiler）

C# 有运行时反射，C++ 没有。Qt 为了在 C++ 中实现"类自省 + 信号槽"，发明了 MOC：
1. 你写的类标了 `Q_OBJECT` 宏
2. qmake 在编译前对头文件运行 `moc.exe`，**扫描 `signals:`/`slots:`/`Q_OBJECT`**，生成 `moc_XXX.cpp`（含元对象表 `QMetaObject`）
3. 元对象表和普通代码一起编译链接

> 这就是为什么：**改了带 Q_OBJECT 的头文件必须重新构建（qmake 会处理）；漏写 Q_OBJECT 则信号槽失效**。

### 本项目落点
- `GD32FirmwareUpdaterQt.pro`：`QT += core gui serialport`、`CONFIG += c++17`
- 编译时观察：`moc_MainWindow.cpp`、`moc_SerialPortService.cpp` 等由 moc.exe 生成在 `build/moc/`

### 练习
在工程目录执行 `qmake && make`，观察 moc 生成物；删除一个 `Q_OBJECT` 后重新编译看会报什么错。

---

## 专题2 QObject 与元对象系统

### WPF 锚点
C# 反射（`typeof(...).GetMethods()`）、`AssemblyInfo.cs`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `[Attribute]` 声明元数据 | `Q_OBJECT` 宏 |
| 反射查询类型信息 | `QMetaObject`（运行时类信息表） |
| `INotifyPropertyChanged` | `Q_PROPERTY` + 变更信号 |
| `object` 基类 | `QObject` 基类（Qt 对象体系之根） |

### 核心原理

所有想要信号槽、父子关系、属性系统的类都继承 `QObject` 并在类内放 `Q_OBJECT`：

```cpp
class SerialPortService : public QObject {
    Q_OBJECT        // 启用元对象系统
public:
    explicit SerialPortService(QObject *parent = nullptr);
signals:            // 信号区：只声明，不实现
    void requestCompleted(const ProtocolFrame &frame);
private slots:      // 槽区：可被 connect 的普通成员函数
    void onReadyRead();
};
```

**QObject 提供的核心能力**：
- `signals:`/`slots:` 关键字 → 元对象系统解析
- 父对象自动回收（专题5）
- `metaObject()` 运行时查询类名/属性/信号列表
- `objectName` / `setProperty` / `property`（类似 WPF 附加属性的简易版）
- `connect` / `disconnect`（专题3）
- `tr()` 国际化（专题12）

### 关键差异：QObject 不可拷贝
QObject 禁止拷贝构造/赋值（C++ 值语义 vs Qt 对象语义）。所以 QObject 一律用指针 + 父子树管理。

### 本项目落点
- `SerialPortService`、`FirmwareUpgradeService`、`UpgradePacketModel`、`MainWindow` 都继承 QObject
- `ProtocolFrame`、`CrcCalculator` **不需要**信号槽 → 保持轻量值类型/静态类

---

## 专题3 信号与槽

### WPF 锚点
`event` / `delegate`、`Button.Click += handler`、`ICommand` / RelayCommand

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `public event Action<int> ProgressChanged` | `signals: void progressChanged(int);` |
| `this.OnProgress += handler` | `connect(sender, &T::sig, receiver, &T::slot)` |
| `btn.Click += btn_Click` | `connect(btn, &QPushButton::clicked, this, &MainWindow::onClick)` |
| `RelayCommand.CanExecute` | 手动 `setEnabled()` |
| 事件在声明类中触发 | 在类内部 `emit progressChanged(...)` |

### 核心原理：连接的 5 种方式

Qt 5 新语法（编译期检查，推荐）：

```cpp
// ① 同对象：发送者→自身槽
connect(m_serialPort, &SerialPortService::requestCompleted,
        this, &FirmwareUpgradeService::onRequestCompleted);

// ② Lambda 槽（最灵活，可捕获上下文）
connect(m_btnConnect, &QPushButton::clicked, this, [this]() {
    onToggleConnect();
});

// ③ 重载信号需显式指定类型（Qt 5 常见坑）
// 如 QComboBox::currentIndexChanged 有 int 和 QString 两个重载
connect(m_comboBaud, QOverload<int>::of(&QComboBox::currentIndexChanged), ...);
```

**连接类型（ConnectionType）** —— 决定槽在哪个线程执行：
- `AutoConnection`（默认）：同线程→直接调用；跨线程→自动转队列连接
- `DirectConnection`：发送信号时立即同步调用（发送者线程）
- `QueuedConnection`：投递到接收者线程事件循环异步调用
- `BlockingQueuedConnection`：跨线程同步等待
- `UniqueConnection`：防重复连接

**与 C# 的关键差异**：
1. C# 事件无脑调用所有订阅者；Qt 槽按连接方式决定同步/异步
2. C# 用 `+=` 隐式多播；Qt 一个信号可连多个槽，一个槽可被多个信号连
3. Qt 信号是**函数声明**（可重载、可传任意类型参数），C# 事件是委托

### 常见坑：信号重载
Qt 5 新语法 `&QComboBox::currentIndexChanged` 有歧义，必须用 `QOverload<int>::of(...)` 或 `static_cast` 消除。

### 本项目落点
- 升级引擎：`emit progressChanged(percent, msg)` 驱动进度条
- 串口：`readyRead` → `onReadyRead` 事件驱动接收
- `FirmwareUpgradeService` 与 `SerialPortService` 通过信号槽解耦

### 练习
给一个按钮连两个槽、再连一个 Lambda，观察执行顺序；用 `sender()` 获取信号发送者。

---

## 专题4 事件循环与事件处理

### WPF 锚点
`Dispatcher` / `Application.Run()`、`RoutedEvent`、`Keyboard/KeyDown`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `Application.Run()` 消息循环 | `app.exec()` 事件循环 |
| `Dispatcher.Invoke` | `QMetaObject::invokeMethod`（跨线程投递） |
| `RoutedEvent` 冒泡/隧道 | `event()` 分发 + `installEventFilter` 过滤 |
| 窗口关闭事件 | `QCloseEvent`、`void closeEvent(QCloseEvent*)` |

### 核心原理：事件循环

`QCoreApplication::exec()` 启动**事件循环**：持续从事件队列取事件、分发给对应对象处理。程序"看起来卡住"其实是循环在等事件。

事件来源：窗口消息、定时器（QTimer）、网络/串口 IO、跨线程信号投递、`postEvent`。

```
事件 → QCoreApplication 派发 → 目标 QObject::event() → 转发给具体事件处理函数
                                                       → 触发关联信号槽
```

### 事件过滤（对比 RoutedEvent 隧道/冒泡）

```cpp
// 给子控件安装事件过滤器，可拦截/吞掉事件
childWidget->installEventFilter(this);

bool MainWindow::eventFilter(QObject *obj, QEvent *ev) {
    if (obj == childWidget && ev->type() == QEvent::KeyPress) {
        // 拦截键盘事件，返回 true 表示已处理（吞掉）
        return true;
    }
    return QWidget::eventFilter(obj, ev);  // 否则放行
}
```

### 本项目落点
- `app.exec()` 驱动整个程序
- 串口数据、超时定时器都靠事件循环派发
- `QTimer::singleShot(2000, ...)` 用事件循环实现延迟（升级跳转等待）

---

## 专题5 对象树与内存管理

### WPF 锚点
GC 自动回收、可视化树（Visual Tree）、`IDisposable`/`using`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| 托管堆 GC | 父子对象树（父销毁时自动销毁子） |
| `new` 后靠 GC | `new QObject(parent)` 挂在树上 |
| `IDisposable` | `deleteLater()`（事件循环安全删除） |
| 引用类型/值类型 | QObject=对象语义（指针）；QString 等=值语义 |

### 核心原理：父子所有权

Qt 用**对象树**管理生命周期，**避免手写 delete 泄露**：

```cpp
// 关键：构造时传入父对象
SerialPortService *svc = new SerialPortService(this);   // this=MainWindow
FirmwareUpgradeService *svc2 = new FirmwareUpgradeService(m_serialPort, this);
```

- MainWindow 销毁时，自动 `delete` 所有子对象
- 控件加到布局里（`addWidget`）也会自动成为父
- **重复 delete 是崩溃源**：确认对象有父/在树上，就不要手动 delete

### deleteLater 的使用场景

在**信号槽内直接 delete 发送者/自己**是危险的（槽还在栈上执行）。Qt 用 `deleteLater()`：把删除推迟到事件循环下一次处理，安全。

```cpp
// 串口关闭时安全释放（本项目 SerialPortService::close）
m_serialPort->deleteLater();
m_serialPort = nullptr;
```

### RAII 智能指针（对比 C# 引用计数）

| 工具 | 用途 | 对应 |
|------|------|------|
| `QSharedPointer` | 共享所有权 | C# 引用计数 |
| `QScopedPointer` | 局部作用域独占 | `using`/局部管理 |
| `QPointer` | 观察指针，目标销毁自动置空 | 弱引用 |

### 本项目落点
- 所有控件 `new Xxx(this)` 挂树，MainWindow 自动管理
- 串口对象 `deleteLater()` 释放
- `QSharedPointer<QMetaObject::Connection>` 管理一次性连接

### 练习
在 MainWindow 析构函数打断点，观察子对象回收顺序（先子后父）。

---

## 专题6 核心类型与容器

### WPF 锚点
`string` / `byte[]` / `List<T>` / `Dictionary<K,V>` / LINQ

### Qt 对应物

| C# 概念 | Qt 概念 |
|---------|---------|
| `string` | `QString`（UTF-16）、`QStringLiteral` |
| `byte[]` | `QByteArray`（串口/二进制核心） |
| `List<T>` | `QList<T>` / `QVector<T>` |
| `Dictionary<K,V>` | `QHash<K,V>` / `QMap<K,V>` |
| LINQ | 迭代器 + `std::algorithm` |
| 引用类型（class） | **值语义**：QString/QByteArray/容器拷贝是浅拷贝 |

### 核心原理：隐式共享（Implicit Sharing）

`QString s1 = s2;` 不复制底层数据！只是**引用计数 +1**，数据真正写入时才深拷贝（写时复制）。

```cpp
QByteArray a = "hello";
QByteArray b = a;   // 共享，O(1)
b[0] = 'H';         // 触发深拷贝，a 不受影响
```

> 对比：C# 字符串也是不可变+引用共享，思想一致。这让 Qt 值类型传参**零成本**，因此**不用到处传指针/引用**。

### 串口/协议必备：QByteArray 操作

```cpp
QByteArray frame;
frame.append(char(0x01));                 // 追加字节
frame.append(data);                        // 追加二进制
frame.mid(offset, len);                    // 切片（对应 Substring）
frame.indexOf(char(0x01));                 // 查找帧头
frame.toHex(' ').toUpper();                // 转 "01 06 00 10" 十六进制
QByteArray::fromHex("01060010");           // 十六进制字符串解析
```

### QString 格式化（对比 string.Format / 插值）

```cpp
QString msg = QStringLiteral("数据包 %1/%2 超时").arg(packetIndex).arg(totalPackets);
QString hex = QStringLiteral("0x%1").arg(value, 8, 16, QLatin1Char('0'));  // 8位补0十六进制
```

### 本项目落点
- 协议帧全程 `QByteArray`（帧组拆、CRC、日志 hex 显示）
- 日志格式化大量用 `QString::arg`
- `QStringList parts; parts.join(".")` 拼版本号

---

## 专题7 Widgets 控件与布局

### WPF 锚点
`StackPanel` / `Grid` / `DockPanel` / `Margin` / `HorizontalAlignment`

### Qt 对应物

| WPF 布局 | Qt 布局管理器 |
|----------|---------------|
| StackPanel(Orientation=Vertical) | `QVBoxLayout` |
| StackPanel(Horizontal) | `QHBoxLayout` |
| Grid | `QGridLayout`（addWidget(控件, 行, 列)） |
| 表单（Label+控件对） | `QFormLayout` |
| Margin / Padding | 布局的 `setContentsMargins` / `setSpacing` |

### 核心原理：布局管理器

**布局管理器（Layout）不是控件**，它负责给子控件分配几何区域，窗口缩放时自动重排。

```cpp
QWidget *central = new QWidget(this);
QVBoxLayout *mainLayout = new QVBoxLayout(central);       // 垂直总布局

QHBoxLayout *row = new QHBoxLayout;                        // 水平行
row->addWidget(new QLabel(QStringLiteral("端口:")));
row->addWidget(m_comboPort);
row->addWidget(m_btnRefresh);
row->addStretch();                                         // 弹性空白（Stretch 对应 WPF 的 *）

mainLayout->addLayout(row);                                // 嵌套布局
setCentralWidget(central);
```

**尺寸策略（SizePolicy）**：控件"如何响应空间变化"：
- `Fixed`：固定大小
- `Expanding`：尽量占满（类似 `HorizontalAlignment=Stretch`）
- `Preferred`：优先理想尺寸，可缩放

### 分组框 QGroupBox

对应 WPF 的 GroupBox，标题自带边框，天然分区：

```cpp
QGroupBox *serialBox = new QGroupBox(QStringLiteral("串口设置"), this);
```

### 本项目落点
- 五区布局全部用 QHBoxLayout/QGridLayout/QVBoxLayout 实现
- `QGroupBox` 分四组：串口/固件/操作/日志
- `mainLayout->addWidget(m_logGroupBox, 1)` 的 `1` 是 stretch 因子，让日志区占满剩余空间

### 练习
把日志区改成上下两个 GroupBox（一个表格、一个日志），观察 stretch 因子对分配的影响。

---

## 专题8 QSS 样式表

### WPF 锚点
`Style` / `ControlTemplate` / `DataTrigger` / `ResourceDictionary`

### Qt 对应物
**QSS（Qt Style Sheets）**：语法完全模仿 CSS，作用类似 WPF 样式模板。

| WPF 概念 | QSS 写法 |
|----------|----------|
| `<Style TargetType="Button">` | `QPushButton { ... }` |
| `Setter Property="Background"` | `background-color: #3b82f6;` |
| `Trigger Property="IsMouseOver"` | `QPushButton:hover { ... }` |
| `Trigger IsEnabled=False` | `QPushButton:disabled { ... }` |
| 模板内边距 | `padding: 6px 16px;` |
| `x:Key` 命名资源 | `#objectName`（`setObjectName`） |

### 核心原理：选择器

```cpp
setStyleSheet(QStringLiteral(R"(
    /* 类型选择器：所有按钮 */
    QPushButton { background-color: #3b82f6; border-radius: 4px; }

    /* 伪状态选择器（对应 Trigger） */
    QPushButton:hover { background-color: #2563eb; }
    QPushButton:disabled { background-color: #b6c2d1; }

    /* ID 选择器：指定对象名的按钮（对应 x:Key 样式） */
    QPushButton#dangerBtn { background-color: #ef4444; }

    /* 后代选择器：GroupBox 内所有 QLabel */
    QGroupBox QLabel { color: #1e293b; }
)"));
```

> **知识点**：QSS 用 `QWidget` 级联继承（子控件继承父的 QSS），与 WPF 资源继承类似；但 QSS 不支持模板（ControlTemplate），复杂自绘要自定义控件 `paintEvent`。

### QSS 用 Raw String Literal
`QStringLiteral(R"( ... )")` 里的 `R"( )"` 是 C++ 原始字符串，多行无需转义，适合写样式。

### 本项目落点
- 主窗口全局 QSS：蓝色主按钮、红色危险按钮（取消升级）、深色日志区、进度条圆角

### 练习
改 `QProgressBar::chunk` 背景色、加 `QComboBox:focus` 边框高亮，实时看效果。

---

## 专题9 Model/View 架构

### WPF 锚点
`ObservableCollection<T>` + `ItemsControl/ListBox/DataGrid` + `DataTemplate` + `IValueConverter`

### Qt 对应物

| WPF 概念 | Qt 概念 |
|----------|---------|
| `ObservableCollection<T>` | `QAbstractItemModel` 子类 |
| `DataGrid` 绑定 ItemsSource | `QTableView` + `setModel()` |
| 集合变化通知（CollectionChanged） | `beginInsertRows` / `dataChanged` / `beginResetModel` |
| `DataTemplate` | `QStyledItemDelegate`（自绘单元格） |
| `IValueConverter` | delegate 的 `displayText()` |
| `INotifyPropertyChanged` | `Q_PROPERTY` + 变更信号 |

### 核心原理：谁驱动刷新？

**WPF**：集合通知 → 界面自动刷（数据驱动）。
**Qt**：View 主动向 Model **索要**数据（`data(row, col, role)`），Model 变化时发通知让 View 重绘。

必须实现的三个纯虚函数：

```cpp
class UpgradePacketModel : public QAbstractTableModel {
    Q_OBJECT
public:
    // ① View 问：有多少行/列？
    int rowCount(const QModelIndex &parent) const override;
    int columnCount(const QModelIndex &parent) const override;
    // ② View 问：这个格子显示什么？（核心，按 role 返回）
    QVariant data(const QModelIndex &index, int role) const override;
    // ③ 表头（可选）
    QVariant headerData(int section, Qt::Orientation, int role) const override;
};
```

**通知 Model 数据变化**（对应 ObservableCollection 的 Add/Update/Clear）：

```cpp
// 插入行：begin 和 end 必须成对，中间填充数据
beginInsertRows(QModelIndex(), row, row);
m_records.append(rec);
endInsertRows();

// 更新某格：告诉 View 该区域脏了
emit dataChanged(topLeft, bottomRight, {Qt::DisplayRole});

// 全清：最重，View 全量重建
beginResetModel();
m_records.clear();
endResetModel();
```

### Role 机制（对应 Binding 多目标）

`data()` 的 `role` 决定"问哪类数据"：
- `Qt::DisplayRole`：显示文本（≈ WPF TextBlock）
- `Qt::DecorationRole`：图标
- `Qt::BackgroundRole` / `ForegroundRole`：颜色
- `Qt::ToolTipRole`：悬浮提示
- 自定义 role：扩展数据

### 本项目落点
- `UpgradePacketModel` 驱动"数据包表格"：每发一包 `addPacket` 插入一行，`updateStatus` 通知状态列刷新

### 练习
给状态列按"成功/失败"返回不同前景色（`Qt::ForegroundRole`）。

---

## 专题10 线程与并发

### WPF 锚点
`Task.Run` / `async/await` / `Dispatcher.Invoke` / `lock`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `Task.Run(() => ...)` | `QtConcurrent::run` / `QThread` |
| `async/await` 顺序化异步 | 信号槽状态机 / `QtConcurrent::run().then` |
| `Dispatcher.Invoke` | `QMetaObject::invokeMethod(obj, ..., Qt::QueuedConnection)` |
| 跨线程更新 UI | **绝对不允许**！必须队列连接回 UI 线程 |
| `lock` / Monitor | `QMutex` / `QMutexLocker` |

### 核心原理：QueuedConnection 跨线程

**线程安全的信号槽**：发送者在工作线程 `emit` 一个信号，若接收者（UI 对象）在另一线程，用 `Qt::QueuedConnection` 把调用**投递到接收者的事件队列**，接收者线程空闲时执行。这正是"安全地切回 UI 线程"：

```cpp
// 工作线程里想更新 UI（必须这样）
QMetaObject::invokeMethod(uiObj, "updateProgress",
                          Qt::QueuedConnection,
                          Q_ARG(int, percent));
```

**对比**：
- `Dispatcher.Invoke` 是"封送回 UI 线程"
- Qt 的 QueuedConnection 是"投递到目标线程事件循环"

**重要铁律**：
1. 所有 UI 操作只能在**主线程**
2. 工作线程信号槽连接用 `Qt::QueuedConnection` 明确指定
3. 跨线程共享数据用 `QMutex` 保护

### 本项目为什么不需要线程？
本项目串口是事件驱动的（QSerialPort 自带后台线程，通过 readyRead 信号回主线程），升级流程用**状态机**，全程不阻塞 → 天然单线程安全。

若固件加密/校验耗时，可 `QtConcurrent::run` 处理，结果队列连接回 UI。

### 练习
用 `QtConcurrent::run` 算一个 CRC，结果通过信号回主线程显示（体会跨线程信号槽）。

---

## 专题11 IO 与串口通讯

### WPF 锚点
`System.IO.Ports.SerialPort` / `DataReceived` 事件 / `await WriteAsync`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `SerialPort` | `QSerialPort` |
| `SerialPort.GetPortNames()` | `QSerialPortInfo::availablePorts()` |
| `DataReceived` 事件 | `readyRead()` 信号 |
| `WriteAsync/ReadAsync` | `write()` / 事件驱动读取 |
| 缓冲区拼包 | 本项目 `tryParseFrame()` 拆帧 |

### 核心原理：串口是字节流

串口**没有帧边界**，必须自己做协议帧解析。核心难点是**粘包（多帧一次到达）和分包（一帧分多次到达）**：

```
接收缓冲:
[帧1字节][帧2前一半][帧2后一半]...
         └─ 分包：先攒够再解析
[帧1][帧2]...
 └─ 粘包：解析完第一帧，缓冲还剩第二帧
```

本项目拆帧算法：
1. `indexOf(SOF)` 定位帧头，丢弃帧头前的噪声
2. 读 `DATALEN` 计算完整帧长
3. 缓冲不够 → 等下次 readyRead（分包）
4. 够 → 截取完整帧 → `deserialize` 校验 → 消费掉（粘包）
5. 用 `remove(0, frameLen)` 把已消费的帧移出缓冲

### 超时管理

`QTimer::singleShot` 实现"发请求→限时等待回复"：
- 回复到了 → `stop()` 定时器
- 超时了 → 发 `requestFailed`，升级引擎决定重试或失败

### 本项目落点
- `SerialPortService` 完整实现拆帧 + 超时
- 升级引擎 `sendRequest(frame, expectedCmd, timeout)` 复用

### 练习
构造连续的两帧数据写入接收缓冲，验证 `tryParseFrame` 能完整解析两帧。

---

## 专题12 调试、国际化与部署

### WPF 锚点
`Debug.WriteLine` / 断点 / 资源文件 / `dotnet publish`

### Qt 对应物

| WPF/C# 概念 | Qt 概念 |
|-------------|---------|
| `Debug.WriteLine` | `qDebug() << "msg"`（输出到控制台/调试器） |
| 日志级别 | `qInfo` / `qWarning` / `qCritical` / `qFatal` |
| 资源文件 .resx | `.qrc` 资源 + `:/path` 引用 |
| `.resx` 国际化 | `tr()` + `.ts`/`.qm` 翻译文件 |
| `dotnet publish` 单文件 | `windeployqt` 收集 DLL |

### 调试技巧

```cpp
qDebug() << "帧数据:" << frame.toHex(' ');       // 打印十六进制
qDebug() << "端口列表:" << ports;                 // 容器直接打印
qWarning() << "串口打开失败:" << err;             // 警告级
```

**断点调试**：Qt Creator 提供完整断点/变量监视/调用栈，与 VS 调试体验一致。

### windeployqt 部署（对应 dotnet publish）

```bash
# 把 exe 所需的全部 Qt DLL 和插件复制到 exe 目录
windeployqt.exe --release --no-translations build/GD32FirmwareUpdaterQt.exe
```

生成内容：
- Qt 模块 DLL（Qt5Core/Gui/Widgets/SerialPort 等）
- MinGW 运行时（libgcc_s_seh-1.dll 等）
- 插件目录（platforms/ styles/ imageformats/）

**发布包 = exe + 同目录所有 DLL**，可整体拷贝到无 Qt 环境的电脑运行。

### 本项目落点
- 日志用信号 `logMessage` 输出到界面（未直接用 qDebug）
- `--smoke` 参数支持自动化冒烟验证
- `windeployqt` 已生成可独立运行的发布包

---

## 附录A：概念速查表（WPF→Qt）

| 你熟悉的 WPF | 你要记的 Qt |
|--------------|------------|
| XAML | 代码布局（或 .ui 文件） |
| INotifyPropertyChanged | Q_PROPERTY + 信号 |
| event/delegate | 信号槽 signals/slots |
| ICommand | 槽函数 + setEnabled |
| Binding Converter | Model data() role / delegate |
| ObservableCollection | QAbstractItemModel |
| Dispatcher.Invoke | QMetaObject::invokeMethod(Queued) |
| async/await | 信号槽状态机 |
| Style/Trigger | QSS 选择器/伪状态 |
| Grid/StackPanel | QGridLayout/QVBoxLayout/QHBoxLayout |
| GC | 父子对象树 |
| .csproj/MSBuild | .pro/qmake |
| System.IO.Ports | QSerialPort + 事件 |
| Debug.WriteLine | qDebug() |
| dotnet publish | windeployqt |

## 附录B：推荐进阶路径

1. 读完本指南 → 能读懂本项目全部代码
2. 动手改项目：加 HEX 固件支持、加超时提示音、改 QSS 皮肤
3. 独立小项目：通讯录管理（练 Model/View）、HTTP 天气客户端（练 QNetworkAccessManager）
4. 深入：阅读 Qt 官方文档 `doc.qt.io`、`Qt Quarterly` 文章、QObject 源码

---

*本指南随项目代码同步交付，代码中以 `【Qt知识点】` 标注的注释可对照本指南学习。*
