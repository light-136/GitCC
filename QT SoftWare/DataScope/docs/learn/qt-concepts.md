# Qt 概念精讲（docs/learn）

> **写法**：每个概念从**工程问题**出发（为什么需要它 → 它解决什么 → 本项目怎么用），
> 而不是罗列 API。读完能回答"为什么 moveToThread"而不仅是"moveToThread 是什么"。

---

## 目录

1. [事件循环：程序为什么"活着"](#1-事件循环程序为什么活着)
2. [信号槽：广播方法](#2-信号槽广播方法)
3. [跨线程：为什么需要线程 / moveToThread / QueuedConnection](#3-跨线程)
4. [元对象系统：Qt 的反射](#4-元对象系统qt-的反射)
5. [Model/View：数据与展示解耦](#5-modelview数据与展示解耦)
6. [协议状态机：工业数据的健壮解析](#6-协议状态机)
7. [QObject 对象树与生命周期](#7-qobject-对象树与生命周期)
8. [单例与全局状态](#8-单例与全局状态)

---

## 1. 事件循环：程序为什么"活着"

**工程问题**：连接是异步的、定时器要周期触发、socket 随时来数据——程序不能"阻塞在一个函数里"，也不能"忙轮询"。

**Qt 解法**：`QApplication::exec()` 进入事件循环。它是一个"无限分发器"：
事件队列里有事件（点击/定时/网络数据/队列信号）就分发，没有就休眠等待。所有异步结果的送达都依赖它。

**核心推论**：
- QTimer 必须在事件循环运行时才触发（`tst_signalslotdemo::clock_emitsAfterEventLoop` 专门验证这点）；
- 跨线程信号到达 = 向对方线程事件循环投递一个"事件"；
- 一个线程里 `exec()` 没跑 → 该线程的异步任务永远不会回调。

**WPF 对照**：`Application.Run()` 的 Dispatcher 消息泵。Qt 的"每个 QThread 自带事件循环"是 WPF 没有的（WPF 只有 UI 线程有 Dispatcher）。

**本项目**：`main.cpp` 的 `app.exec()`；采集线程里 `QThread` 启动后自己跑事件循环（`moveToThread` 的 Worker 的槽才能被投递执行）。

---

## 2. 信号槽：广播方法

**工程问题**：生产者（采集线程）不知道谁关心数据；消费者（监控页/记录器）不该反向依赖生产者。需要**发布-订阅**。

**Qt 解法**：信号是 **public 成员函数**（`emit xxx()` 直接调用它，触发所有已连接槽）；`connect(sender, &S::sig, receiver, &R::slot)` 完成订阅。

**关键认知**：
- 同线程：`emit` 是**同步直调**——槽在 emit 的栈帧里执行完才返回；
- 信号可以连多个槽（一对多广播），一个槽可以被多个信号连；
- 信号函数体由 moc 生成——你只写声明 + `emit`，不写定义。

**WPF 对照**：事件（`event Action`）+ 委托订阅。区别：WPF 事件是字段型多播委托；Qt 信号是**成员函数 + moc 元数据**，能跨线程自动排队、能被 QSignalSpy 探针监听。

**本项目**：`SignalSlotDemo`（`src/app/signalslotdemo.cpp`）演示五种连接写法；`LogManager::messageLogged` 是所有模块的"日志广播站"（观察者模式）。

---

## 3. 跨线程：为什么需要线程 / moveToThread / QueuedConnection

**工程问题**（`src/services/`）：
1. **为什么需要线程**：采集是持续收发的重活，放在 UI 线程会让界面卡死（点按钮没反应）；
2. **为什么 moveToThread**：Qt 要求对象在**它的线程**里创建（尤其 socket）。把 Worker `moveToThread(thread)` 后，Worker 的槽就在采集线程执行；
3. **为什么 QueuedConnection**：两个线程的对象用 `connect`（默认 AutoConnection 会判定跨线程 → 队列连接），`emit pointsReady(...)` 不会立刻执行槽，而是**打包参数投递**到主线程事件循环，主线程有空再执行——天然线程安全。

**为什么参数要 Q_DECLARE_METATYPE**：队列连接要把参数**拷贝**进事件。拷贝前提是 Qt 认识这个类型——用 `Q_DECLARE_METATYPE` 声明 + `qRegisterMetaType` 注册（多名字兜底，保证 moc 规范化名一致）。

**WPF 对照**：Dispatcher.Invoke 手动编回 UI 线程 vs Qt 队列连接自动投递。Qt 的"自动"是因为信号槽本身就携带线程信息。

**本项目**：`DataService`（主线程）→ `AcquisitionWorker`（采集线程），`QVector<DataPoint>`、`QVector<ChannelConfigInfo>` 都做了元类型注册。UI 侧**零跨线程代码**——所有数据都以信号到达主线程。

---

## 4. 元对象系统：Qt 的反射

**工程问题**：需要知道"这个对象有什么信号/槽/属性"（动态查询）、需要字符串调用方法（invokeMethod）、需要 QSS 按属性样式化——C++ 没有内建反射。

**Qt 解法**：类声明 `Q_OBJECT` 宏 → moc 编译器生成元对象代码（`staticMetaObject`）。运行时 `QMetaObject::invokeMethod(obj, "start", QueuedConnection)` 就是反射调用；`connect(ptr, SIGNAL(...), ...)` 字符串式连接也靠它。

**WPF 对照**：`typeof/GetType()` 反射 + `DependencyObject.GetValue`。Qt 的元对象**专门为信号槽优化**（不只是通用反射），且是编译期生成的（性能可控）。

**本项目**：`DataService::connectTo` 里 `QMetaObject::invokeMethod(m_worker, "start", Qt::QueuedConnection, ...)` ——把"start 槽"按名字投递到采集线程执行，正是元对象系统的用法。

---

## 5. Model/View：数据与展示解耦

**工程问题**：设备列表、报警列表要**和 UI 分离**（可单测、可换 View），增删数据时 View 要自动刷新。

**Qt 解法**：Model 实现 `rowCount/data`（data 按 role 返回），View（`QListView/QTableView`）setModel 后自动取数绘制。数据变化：插入用 `beginInsertRows/endInsertRows`，更新用 `emit dataChanged`。

**WPF 对照**：ObservableCollection + Binding。区别：WPF 集合是现成的；Qt Model 是接口，**你自己当容器**（`m_events`、设备向量），但换来零耦合——同一 Model 可接多种 View。

**本项目**：`AlarmEventModel`（`src/ui/models/`）接 `QListView` 显示报警；`DeviceListModel` 接 `QTableView` 显示设备。报警引擎只发事件给 Model，**完全不知道 View 存在**（可单测）。

---

## 6. 协议状态机：工业数据的健壮解析

**工程问题**：TCP 是字节流，一帧可能被拆成多次到达（半包），也可能一次到多帧（粘包），还可能夹带垃圾/坏帧。逐字节解析 + 重同步是基本功。

**Qt 解法**：`FrameParser` 三态状态机（`src/protocol/frameparser.cpp`）：
- **扫帧头**：找 `AA 55`，之前的垃圾丢弃；
- **读帧头**：读 FUNC/CMD/LEN；LEN 超上限（1024）→ 判非法帧头，丢弃重扫（**不撑爆缓冲**）；
- **收数据校验**：缓冲够了一整帧 → CRC 校验，通过产出、失败丢弃。

**为什么是状态机不是简单 read**：`readyRead` 触发时可能只到了半帧；简单"读长度再读全"遇到粘包会卡住。状态机 + 累积缓冲 + 按需 `nextFrame()` 才能天然处理所有组合。

**WPF 对照**：没有直接对应（WPF 不太做裸字节协议）。这是**工业软件专属**的健壮性训练。

**本项目**：`TcpClient::onReadyRead` 调 `parser.feed(readAll())` 再循环 `nextFrame()`；模拟设备的 `FaultInjector` 专门造"粘包/分包/坏 CRC/非法帧"来考验它（`tst_faultinjector`、`tst_simulatordevice`）。

---

## 7. QObject 对象树与生命周期

**工程问题**：控件父子关系、信号槽连接的生命期、线程对象的销毁顺序——C++ 手动管理会泄漏/悬垂。

**Qt 解法**：
- **对象树**：`QObject` 有 parent；parent 析构时**自动 delete 所有 child**。`new QWidget(parent)` 后无需手动释放；
- **deleteLater()**：在事件循环下一次处理时安全删除——用于"不能在当前栈帧里 delete"的场景（布局遍历、信号处理中）。

**WPF 对照**：没有对象树——WPF 靠 GC + 可视树。Qt 的对象树是**所有权模型**：谁创建谁负责（交给父对象托管）。

**本项目**：
- `MainWindow` 里所有页面/服务 `new xxx(this)`——析构一条链回收（`MainWindow::~MainWindow = default`）；
- `MonitorPage::rebuildChannelArea` 里旧卡片 `w->deleteLater()`——布局遍历中删除不悬垂；
- `AcquisitionWorker::start` 里 `new TcpClient(this)`——socket 随 Worker 生命周期。

---

## 8. 单例与全局状态

**工程问题**：日志、配置这类"全应用一份"的基础设施，要全局可达、线程安全。

**Qt 解法**：**Meyers 单例**（函数内 static 局部变量）：`static LogManager &instance() { static LogManager m; return m; }`——C++11 保证线程安全的懒初始化。

**WPF 对照**：`Application.Current` / 静态服务定位器 / 依赖注入容器。Qt 更朴素——单例即够，大项目才上 DI 容器。

**本项目**：`LogManager::instance()`、`ConfigManager::instance()`（`src/infrastructure/`）。日志单例内部用 `QMutex` 保护写盘，跨线程安全；`messageLogged` 信号广播给日志面板。

---

## 自测清单（学完本章应能回答）

- [ ] 为什么 QTimer 不触发？→ 事件循环没跑
- [ ] 为什么跨线程信号没生效？→ 元类型没注册 / 参数名不一致
- [ ] 为什么 socket 要在 Worker 线程创建？→ 线程亲和铁律
- [ ] 为什么粘包不会丢帧？→ 状态机 + 累积缓冲
- [ ] 为什么 `deleteLater` 而不是 `delete`？→ 事件处理栈帧里不能立刻析构

---

*下一篇：`exercises.md`（阶段练习）——Level 1-7 的练习与验收。*
