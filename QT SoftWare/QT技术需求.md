# Qt 系统化学习与完整工程实战项目需求书

## 一、项目背景

我目前是一名有较丰富 WPF / C# 开发经验的软件开发者。

我对以下内容比较熟悉：

* C#
* .NET
* WPF
* MVVM
* 面向对象编程
* 异步编程
* 多线程
* TCP / Socket
* 串口通信
* 数据解析
* 状态机
* UI 开发
* 软件工程
* 工业软件开发

因此，我学习 Qt 的目的并不是从“什么是变量、什么是类”这种编程基础开始，而是希望：

> **利用我已经掌握的 WPF / C# 知识作为认知锚点，系统迁移到 Qt / C++，最终真正具备独立使用 Qt 开发中大型桌面软件的能力。**

我希望你不要把这件事情理解成：

> “给我讲 Qt 教程。”

而应该理解成：

> **你是一名顶级 Qt 工程师，同时也是我的 Qt 技术导师。你需要设计并完整开发一个具有真实工程价值的中大型 Qt 项目，并把 Qt 的核心知识体系尽可能完整地融入这个项目中。**

最终目标是：

> **项目完成 ≠ 学习结束。**
>
> 我要能够通过这个项目理解 Qt 的设计思想、核心机制、工程结构和实际开发方法，并最终能够自己从零开始开发 Qt 软件。

---

# 二、你的角色

从现在开始，请同时扮演以下三个角色。

## 1. 顶级 Qt 工程师

负责：

* Qt 架构设计
* C++ 工程设计
* Qt Widgets
* Qt Core
* Qt GUI
* Qt Network
* Qt SerialPort
* Qt Concurrent / 多线程
* Model/View
* Signal / Slot
* Event Loop
* QThread
* QTimer
* QIODevice
* QTcpSocket
* QSerialPort
* QSettings
* JSON
* 文件系统
* 日志系统
* 配置系统
* CMake / qmake
* Qt Creator
* Windows 部署
* Qt 工程规范
* 可维护性
* 可扩展性
* 软件架构

## 2. Qt 教师

不能只是给我代码。

你需要让我理解：

> 为什么这么设计？

> Qt 为什么这么设计？

> 它和 WPF 有什么区别？

> C# 中这个机制，在 Qt / C++ 中对应什么？

> 什么时候应该这样做？

> 什么时候不能这样做？

> 如果不用 Qt 的这个机制，还有什么实现方式？

> 工业级项目中通常怎么做？

## 3. 软件架构师

这个项目不能为了“展示 Qt 知识”而堆砌功能。

必须按照真实软件工程方式设计：

```text
需求
 ↓
架构
 ↓
模块
 ↓
接口
 ↓
实现
 ↓
测试
 ↓
日志
 ↓
异常处理
 ↓
部署
 ↓
维护
```

---

# 三、最重要的学习原则

## 原则 1：不要脱离项目学习

不要采用：

```text
第一章 Qt 是什么
第二章 QPushButton
第三章 QLabel
第四章 QTimer
第五章 QThread
...
```

这种纯教程方式。

而应该：

```text
真实需求
 ↓
为什么需要这个功能
 ↓
设计方案
 ↓
Qt 技术
 ↓
代码实现
 ↓
运行验证
 ↓
总结
 ↓
与 WPF 对比
```

让我在真实项目中学习 Qt。

---

# 四、必须建立 WPF → Qt 的知识迁移体系

由于我已经熟悉 WPF，因此请你充分利用这一优势。

以后讲 Qt 时，尽量建立这样的认知映射：

| WPF / C#                        | Qt / C++                                      |
| ------------------------------- | --------------------------------------------- |
| Window                          | QWidget / QMainWindow                         |
| UserControl                     | QWidget                                       |
| XAML                            | Qt Designer / C++ UI                          |
| Binding                         | Model/View / Property / Signal-Slot           |
| ICommand                        | Signal/Slot + Command Pattern                 |
| MVVM                            | Qt Model/View + 自定义架构                         |
| ObservableCollection            | QList / QVector + Model                       |
| INotifyPropertyChanged          | Signal                                        |
| DependencyProperty              | QObject Property                              |
| Dispatcher                      | Event Loop                                    |
| async/await                     | Signal/Slot / QFuture / QtConcurrent / Thread |
| Task                            | QFuture / Thread                              |
| CancellationToken               | QAtomic / 状态控制 / 自定义取消机制                      |
| Timer                           | QTimer                                        |
| SerialPort                      | QSerialPort                                   |
| TcpClient                       | QTcpSocket                                    |
| NetworkStream                   | QIODevice / QTcpSocket                        |
| File                            | QFile                                         |
| Directory                       | QDir                                          |
| JSON                            | QJsonDocument / QJsonObject                   |
| Configuration                   | QSettings                                     |
| DataGrid                        | QTableView + Model                            |
| ObservableCollection + DataGrid | QAbstractTableModel + QTableView              |
| Dependency Injection            | Qt 工程中的接口 + Composition                       |
| UserControl                     | QWidget                                       |
| Routed Event                    | Qt Event / Signal-Slot                        |
| Style                           | QSS                                           |
| ResourceDictionary              | Qt Resource System                            |
| App.xaml                        | QApplication                                  |
| Main()                          | main.cpp                                      |
| NuGet                           | CMake / vcpkg / Conan 等                       |
| .csproj                         | CMakeLists.txt / .pro                         |

但是：

**不要强行认为 WPF 和 Qt 一一对应。**

对于没有直接对应关系的地方，请明确告诉我：

> “这里不要用 WPF 思维理解。”

这是我学习 Qt 非常重要的一点。

---

# 五、项目必须是“完整软件”，不能只是 Demo 集合

请你自行设计一个具有真实工程价值的 Qt 桌面软件。

软件题材你可以根据你的知识和教学目标选择。

但是必须满足：

1. 有真实业务流程
2. 有复杂 UI
3. 有数据模型
4. 有配置
5. 有日志
6. 有文件读写
7. 有数据解析
8. 有通信
9. 有异步任务
10. 有线程
11. 有状态机
12. 有错误处理
13. 有 Model/View
14. 有自定义控件
15. 有信号槽
16. 有定时器
17. 有网络或串口通信
18. 有测试
19. 有日志追踪
20. 可以独立发布
21. 可以继续扩展
22. 项目结构符合真实工程习惯

你可以自行选择最适合教学的项目主题。

但是：

> **不要为了迁就我过去的 WPF / 工业背景而设计项目。**

项目首先应该服务于：

> **完整学习 Qt。**

如果某个领域能够自然覆盖 Qt 技术，则可以使用。

---

# 六、项目必须覆盖的 Qt 知识体系

请你在项目规划阶段建立完整的 Qt 知识地图。

至少覆盖以下内容。

## 第一层：Qt 基础

* Qt 是什么
* Qt 的整体架构
* Qt Module
* QObject
* Parent / Child
* Object Tree
* Meta Object System
* MOC
* Signal
* Slot
* connect
* emit
* Event
* Event Loop
* QApplication
* QCoreApplication
* QMainWindow
* QWidget

---

# 七、C++ 与 Qt

因为我虽然熟悉 C#，但我要真正掌握 Qt，因此不能只教 Qt API。

必须同步让我掌握 Qt 开发所需要的现代 C++。

包括：

* 指针
* 引用
* const
* const reference
* stack / heap
* RAII
* 析构
* 构造
* copy
* move
* move semantics
* smart pointer
* unique_ptr
* shared_ptr
* weak_ptr
* lambda
* template
* STL
* vector
* map
* unordered_map
* string
* exception
* enum
* struct
* class
* inheritance
* polymorphism
* virtual
* override
* interface
* header / cpp
* namespace
* include
* forward declaration
* CMake

尤其要不断解释：

> C# 的 GC 思维和 C++ RAII 思维到底有什么本质区别。

---

# 八、Qt UI 系统

必须完整覆盖：

* QWidget
* QMainWindow
* Layout
* QLabel
* QPushButton
* QLineEdit
* QTextEdit
* QComboBox
* QCheckBox
* QRadioButton
* QSpinBox
* QDoubleSpinBox
* QListWidget
* QTreeWidget
* QTableWidget
* QTableView
* QDialog
* QMessageBox
* QFileDialog
* QTabWidget
* QSplitter
* QGroupBox
* QScrollArea
* StatusBar
* MenuBar
* ToolBar

同时学习：

* Qt Designer
* UI 文件
* 手写 UI
* UI 与代码的关系
* Layout 管理
* Size Policy
* Size Hint
* Minimum / Maximum Size
* DPI
* 高 DPI
* 窗口生命周期

---

# 九、Model/View

这是重点。

必须让我真正掌握：

* QAbstractItemModel
* QAbstractTableModel
* QAbstractListModel
* QModelIndex
* rowCount
* columnCount
* data
* setData
* flags
* headerData
* beginInsertRows
* endInsertRows
* beginRemoveRows
* endRemoveRows
* dataChanged
* layoutChanged

并且让我理解：

> QTableWidget 和 QTableView + QAbstractTableModel 到底有什么区别。

最好在项目中实际实现一个复杂的数据表格。

---

# 十、Signal / Slot

必须深入学习：

* Signal
* Slot
* connect
* disconnect
* lambda slot
* connection type
* DirectConnection
* QueuedConnection
* AutoConnection
* 跨线程 Signal / Slot
* QObject::connect
* sender / receiver
* 生命周期
* deleteLater

同时与 C#：

```csharp
event
delegate
Action
Func
async/await
```

进行比较。

---

# 十一、事件系统

必须学习：

* Event Loop
* QApplication
* QEvent
* mouse event
* keyboard event
* paint event
* resize event
* close event
* focus event
* timer event
* eventFilter
* 自定义事件

并在项目中实际使用至少一部分。

---

# 十二、线程与异步

这是重点。

必须完整学习：

* QThread
* QObject + moveToThread
* Worker Pattern
* QThreadPool
* QRunnable
* QtConcurrent
* QFuture
* QFutureWatcher
* Signal / Slot 跨线程
* QueuedConnection
* GUI Thread
* Worker Thread
* 线程安全
* Mutex
* QMutex
* QReadWriteLock
* QWaitCondition
* 原子操作
* 数据竞争
* 死锁
* Race Condition

必须让我真正理解：

> Qt 中为什么不能直接在 Worker Thread 操作 QWidget。

并解释它与 WPF Dispatcher / UI Thread 的区别。

---

# 十三、Timer

必须使用：

* QTimer
* timeout
* singleShot
* 定时任务
* 超时机制
* 状态机超时
* 通信超时

让我理解：

> QTimer 本质上依赖什么？

---

# 十四、网络通信

项目中必须包含实际通信功能。

学习：

* QTcpSocket
* QTcpServer
* QUdpSocket
* QHostAddress
* QNetworkInterface
* QNetworkAccessManager
* HTTP
* JSON
* TCP 粘包
* TCP 分包
* 数据缓存
* 协议解析
* 超时
* 重连
* 错误处理

最好实现一个完整的通信协议。

---

# 十五、串口

必须学习：

* QSerialPort
* QSerialPortInfo
* 打开串口
* 波特率
* 数据位
* 停止位
* 校验位
* readyRead
* write
* waitForReadyRead
* 超时
* 串口数据缓存
* 粘包 / 分包
* 协议解析

---

# 十六、文件与数据

必须学习：

* QFile
* QDir
* QFileInfo
* QTextStream
* QDataStream
* QByteArray
* QString
* QStringList
* QBuffer
* JSON
* XML（如果合理）
* CSV
* 二进制文件

同时学习 Qt 类型与 STL 类型之间的关系。

---

# 十七、配置系统

实现完整配置：

* QSettings
* INI
* JSON 配置
* 默认配置
* 配置加载
* 配置保存
* 配置校验
* 配置版本升级

---

# 十八、日志系统

不要简单使用 printf。

实现一个真正的软件日志体系：

```text
DEBUG
INFO
WARNING
ERROR
CRITICAL
```

至少包括：

* 时间
* 模块
* 线程
* 日志级别
* 内容
* 文件保存
* 日志轮转
* 异常日志

并让我理解 Qt：

```cpp
qDebug()
qInfo()
qWarning()
qCritical()
```

与真实工程日志系统之间的区别。

---

# 十九、状态机

必须实际设计一个复杂状态机。

例如：

```text
Idle
 ↓
Connecting
 ↓
Connected
 ↓
Preparing
 ↓
Running
 ↓
Completed
 ↓
Error
 ↓
Retry
 ↓
Running
```

学习：

* 状态
* 状态转换
* 状态事件
* 状态超时
* 状态恢复
* 重试
* 错误状态

如果适合，可以进一步介绍：

* QStateMachine

以及：

> 自己实现状态机 vs Qt 状态机框架。

---

# 二十、Qt 样式

必须学习：

* QSS
* StyleSheet
* Selector
* Property
* Palette
* Font
* Icon
* Resource System
* .qrc

最终做出一个具有完整视觉体系的软件，而不是默认 Qt 控件堆起来。

---

# 二十一、架构

项目必须采用合理的分层架构。

例如：

```text
Application
│
├── UI
│
├── ViewModel / Presentation
│
├── Services
│
├── Domain
│
├── Protocol
│
├── Infrastructure
│
├── Models
│
└── Utils
```

但不要机械照搬 MVVM。

我要学习：

> **Qt 应该如何做架构。**

尤其需要解释：

* Qt 为什么不像 WPF 一样天然围绕 MVVM
* Qt Model/View 和 WPF MVVM 的差异
* Signal/Slot 在架构中的位置
* Service 如何设计
* UI 与业务逻辑如何解耦
* 如何避免 QWidget 变成 God Object

---

# 二十二、工程构建

必须学习现代 Qt 工程：

优先使用：

```text
CMake
```

同时介绍：

```text
qmake
```

让我理解：

* CMakeLists.txt
* target
* include
* link
* Qt modules
* Debug / Release
* 编译
* 构建目录
* 依赖
* 第三方库
* 静态 / 动态链接

---

# 二十三、测试

项目必须有测试。

至少包括：

* 单元测试
* 协议测试
* 数据解析测试
* 边界测试
* 错误测试
* 通信测试
* 冒烟测试

如果适合，学习：

* Qt Test

最终让我理解：

> Qt 项目如何做自动化测试。

---

# 二十四、部署

必须最终真正发布。

需要学习：

* Debug / Release
* windeployqt
* DLL
* Qt plugins
* platform plugins
* imageformats
* 配置文件
* 资源文件
* 第三方 DLL
* 独立运行
* 发布目录

最终要求：

> **把项目复制到一台没有 Qt 开发环境的 Windows 电脑上，可以正常运行。**

---

# 二十五、开发过程要求

不要一次性把全部代码扔给我。

必须按照真实软件开发流程推进：

```text
阶段 1：需求分析
        ↓
阶段 2：技术选型
        ↓
阶段 3：总体架构
        ↓
阶段 4：项目骨架
        ↓
阶段 5：核心模块
        ↓
阶段 6：UI
        ↓
阶段 7：通信
        ↓
阶段 8：线程
        ↓
阶段 9：状态机
        ↓
阶段 10：日志
        ↓
阶段 11：测试
        ↓
阶段 12：优化
        ↓
阶段 13：部署
        ↓
阶段 14：总结
```

每个阶段完成后：

1. 告诉我本阶段学习了什么
2. 告诉我 Qt 的核心知识
3. 与 WPF 对比
4. 指出代码位置
5. 让我运行验证
6. 给出练习
7. 再进入下一阶段

---

# 二十六、必须输出完整项目文档

项目开始时，先不要直接写代码。

先为我建立完整的文档体系。

至少包括：

### 01-项目规划文档

包括：

* 项目背景
* 学习目标
* 软件目标
* 技术目标
* Qt 知识覆盖范围
* 项目阶段
* 最终能力目标

### 02-总体设计文档

包括：

* 系统架构
* 模块划分
* 类关系
* 数据流
* 线程模型
* 通信模型
* 状态机
* UI 架构

### 03-Qt学习地图

这是最重要的学习文档。

需要把：

```text
Qt知识
 ↓
项目模块
 ↓
源码位置
 ↓
WPF对应知识
```

全部建立映射。

### 04-WPF → Qt 对照指南

把我熟悉的 WPF 知识作为锚点。

但同时明确：

> 哪些地方可以类比。

> 哪些地方不能类比。

### 05-开发规范

包括：

* C++ 规范
* Qt 规范
* 命名
* 文件组织
* 类设计
* Signal / Slot
* 内存管理
* 线程安全
* 错误处理
* 日志规范

### 06-测试文档

包括：

* 测试策略
* 测试用例
* 单元测试
* 集成测试
* 冒烟测试
* 发布测试

### 07-使用说明

最终用户能够按照文档运行软件。

### 08-项目总结

项目完成后总结：

* 学到了什么
* 哪些 Qt 技术已经掌握
* 哪些仍需要继续学习
* 下一阶段应该学习什么

---

# 二十七、教学必须深入代码

不要只告诉我：

```cpp
connect(button, &QPushButton::clicked, this, &MainWindow::onClicked);
```

然后说：

> “这是信号槽。”

我要你解释：

```text
clicked 是什么？
为什么是 Signal？
为什么需要 connect？
this 是什么？
成员函数为什么可以作为 Slot？
lambda 怎么写？
Qt 的元对象系统在哪里？
MOC 在做什么？
跨线程的时候会发生什么？
```

让我理解机制，而不是记 API。

---

# 二十八、每一个重要 Qt 技术都必须回答五个问题

以后讲任何重要技术，都尽量按照：

### 1. 它是什么？

### 2. 为什么 Qt 需要它？

### 3. 它解决了什么问题？

### 4. WPF / C# 中对应什么？

### 5. 在真实项目中什么时候使用？

这样教学。

---

# 二十九、不要为了“覆盖知识点”而强行使用技术

这是一个非常重要的要求。

例如：

如果项目根本不需要某个 Qt 模块，不要为了完成“知识点清单”硬塞进去。

可以明确告诉我：

> “这个项目暂时没有合理使用场景，所以这里只做原理介绍，不强行加入。”

我要学习的是：

> **工程判断能力**

而不是：

> **API 背诵能力。**

---

# 三十、最终能力目标

这个项目完成以后，我希望达到：

### Level 1

能够看懂 Qt 项目。

### Level 2

能够修改 Qt 项目。

### Level 3

能够独立开发 Qt 功能模块。

### Level 4

能够从零建立 Qt 桌面项目。

### Level 5

能够设计 Qt 中型软件架构。

### Level 6

能够处理：

* UI
* 网络
* 串口
* 多线程
* Model/View
* 状态机
* 文件
* 配置
* 日志
* 测试
* 部署

### Level 7

能够阅读大型 Qt 项目源码，并理解其架构。

---

# 三十一、最终项目验收标准

项目完成时必须能够回答：

```text
Qt 到底是什么？

QObject 为什么重要？

Signal / Slot 为什么存在？

MOC 是什么？

Event Loop 是什么？

QTimer 为什么能够工作？

QThread 到底是什么？

QObject + moveToThread 为什么常见？

为什么不能直接跨线程操作 QWidget？

QAbstractTableModel 为什么存在？

QModelIndex 是什么？

Qt Model/View 和 WPF MVVM 有什么区别？

QByteArray 与 std::vector<char> 有什么区别？

QString 与 std::string 有什么区别？

Qt 的内存管理是什么思想？

Qt 项目如何管理生命周期？

Qt 如何做 TCP？

Qt 如何做串口？

Qt 如何解决 TCP 粘包？

Qt 如何实现状态机？

Qt 如何做日志？

Qt 如何做配置？

Qt 如何测试？

Qt 如何发布？

CMake 如何组织 Qt 项目？

```

如果这些问题我都能够自己解释，并且能够独立写出相应代码，那么这个学习项目才算真正完成。

---

# 三十二、最终要求

请你现在不要直接开始写代码。

第一步请先完成：

## 《Qt 完整学习 + 实战项目总体规划》

内容必须包括：

1. 你为我选择的最终项目
2. 为什么选择这个项目
3. 软件最终形态
4. 功能模块
5. 总体架构
6. 技术栈
7. Qt 知识覆盖地图
8. WPF → Qt 知识迁移地图
9. 项目开发阶段
10. 每个阶段对应的学习目标
11. 文档体系
12. 测试体系
13. 发布体系
14. 最终能力模型
15. 后续扩展路线

**先规划，不要急着编码。**

规划完成以后，我们再按照阶段逐步开发。

整个过程请始终牢记：

> **这个项目不是单纯为了做出一个软件。**
>
> **软件是载体，真正的目标是让我通过这个软件系统掌握 Qt。**

你需要以：

> **“顶级 Qt 工程师 + 顶级软件架构师 + Qt 教师”**

的标准来设计整个项目。

不要敷衍，不要只给我一个 Demo，不要只教 API。

我要的是：

> **一个真正完整、可运行、可测试、可部署、可扩展，同时能够覆盖 Qt 核心知识体系的工程化学习项目。**

现在开始进行项目总体规划。
