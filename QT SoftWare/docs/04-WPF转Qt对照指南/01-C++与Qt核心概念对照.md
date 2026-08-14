# 《WPF→Qt 对照指南 · 基础篇（C++ 与 Qt 核心概念）》

> 文档编号：04-01　建立：P2　修订：随阶段更新
> 面向读者：熟悉 C#/WPF、不懂 Qt 的开发者（正在通过 DataScope Studio 学习 Qt）
> 配套工程：DataScope Studio（Qt 5.12 Widgets + C++17 + CMake）
> 配套代码：`DataScope/src/app/main.cpp`、`DataScope/src/ui/mainwindow.*`、`DataScope/src/utils/byteutils.h`

---

## 0. 这份文档怎么读

本工程（`QT SoftWare/docs/04-WPF转Qt对照指南/`）的目标，是给你一张**「可类比 vs 不可类比」**的地图：哪些 WPF/C# 概念可以平移过来用，哪些表面像、骨子里完全不是一回事。

每一节统一用下面的结构：

1. **一句话类比** —— 用最省力的话说清"这俩到底是什么关系"；
2. **代码对比** —— C# 一行 + C++/Qt 一行，先看差异再看解释；
3. **机制讲解** —— 讲**为什么**，不堆 API；
4. **为什么不能简单照搬** —— 你按 C# 惯性会踩的坑；
5. **在 DataScope 里的落地** —— 对照本工程真实代码，让概念挂到实处。

**可类比程度（★）图例**（满星 5 颗）：

| 星数 | 含义 | 典型例子 |
|------|------|----------|
| ★★★★★ | 概念完全对应，几乎可以平移 | namespace ↔ 命名空间 |
| ★★★★☆ | 思想一致，但使用方式有明显差异 | event ↔ 信号槽 |
| ★★★☆☆ | 能力对等，但心智模型不同 | LINQ ↔ STL 算法 |
| ★★☆☆☆ | 表面相似，机制完全不同 | GC ↔ RAII |
| ★☆☆☆☆ | 只是名字像，本质无关 | struct |

**最重要的开篇提醒**：C# 是一座"托管城市"——内存、生命周期、线程上下文都由运行时托管，你只管写业务。C++ 是一片"自建荒地"——每一块资源的生命周期都要你亲自画清楚。所以本基础篇的第一课不是语法，而是**所有权**。

---

## 1. 垃圾回收 GC vs RAII

> 可类比程度：★★☆☆☆　（都"不用手动管内存"，但机制与时机完全相反）

**一句话类比**：C# 的 GC 是"定时清洁工"，你不知道它几点来；C++ 的 RAII 是"出门必锁门"，作用域一结束立刻收拾干净。

**代码对比**

```csharp
// C#：堆上分配，GC 兜底，没人关心它什么时候被回收
var data = new List<byte>();
```

```cpp
// C++：栈上分配，离开作用域自动析构——不需要也找不到 "delete"
QByteArray data;   // 函数返回的那一刻，析构函数自动运行
```

**机制讲解**

- **C# 的 GC**：`new` 出来的对象在托管堆上，GC 从"根集"（局部变量、静态字段、寄存器等）出发追踪可达性，不可达即回收。回收**时机不确定**（内存压力、第 0 代满、显式 `GC.Collect`）。程序员几乎不感知生命周期，代价是无法精确控制释放时机——`IDisposable` 就是给"需要确定性释放"的资源（文件、数据库连接）打的补丁。
- **C++ 的 RAII**（Resource Acquisition Is Initialization，资源获取即初始化）：构造函数获取资源，析构函数释放资源。对象在栈上时，作用域结束（函数返回、`{}` 块结束、循环体结束）**立即**运行析构函数——确定性销毁，分毫不差。`QByteArray`、`QString`、`QFile`、`QMutexLocker` 全是 RAII 类。

**为什么不能简单照搬**

1. C# 的"`new` 了就放心，GC 会收"在 C++ 里是错的。C++ 裸 `new` 必须配套 `delete`，`new` 得越多、`delete` 责任越分散，越容易漏或重删。
2. GC 的"不确定"在 C# 里无害（有引用就不会被回收）；C++ 的析构是**确定**的，你必须能在脑中模拟"谁先析构、谁后析构、析构顺序"。
3. 反过来，C++ 的确定性析构能做 C# 做不到的事：锁、文件句柄、数据库连接"离开作用域自动关"。C# 得靠 `using` 块；C++ 写类时把清理写进析构函数就行，之后使用者想漏都漏不掉。

**在 DataScope 里的落地**

- `main.cpp` 里 `MainWindow window;` 是**栈对象**，`main` 返回时自动析构，无需 `delete`——这正是 P1 强调的 RAII 心智模型。
- `byteutils.h` 里所有返回 `QByteArray`/`QString` 的函数都是 RAII：返回的是值，调用方用完自动释放。

---

## 2. 引用类型 vs 值类型 / 值语义

> 可类比程度：★★★☆☆　（C# 的 class 引用 ≈ C++ 的指针；但 C++ 默认是值，必须显式才共享）

**一句话类比**：C# 的变量是"遥控器"，C++ 的变量是"电视机本身"。

**代码对比**

```csharp
var a = new Config();   // a 是引用（遥控器），对象在堆上
var b = a;              // b 和 a 指向同一个对象
b.ChannelCount = 8;     // a 也能看到这个修改（同一个对象）
```

```cpp
Config a;               // a 是对象本体（电视机），就在这块内存里
Config b = a;           // b 是 a 的副本：拷贝构造，生成第二台电视机
b.channelCount = 8;     // a 完全不受影响
```

**机制讲解**

- **C#**：`class` 是引用类型。变量保存的是**引用**（可以理解为受 GC 托管的指针），对象实体在堆上。赋值 = 拷贝引用，两个变量指向同一对象，别名共享。
- **C++**：默认**值语义**。变量就是对象本身，占自己大小的内存。赋值 = 拷贝对象内容（调用拷贝构造函数），是两个独立个体。想在 C++ 里共享同一实例，必须显式使用指针、引用或智能指针。

**为什么不能简单照搬**

1. **容器存的是副本**：`list.Add(obj)` 在 C# 存引用（之后改 obj 容器里跟着变）；C++ 里 `vec.push_back(obj)` 存**副本**，改原 obj 不影响容器内的。这条是 C# 转 C++ 最常见的第一撞。
2. **共享要显式**：想保持"多个地方看同一个对象"，必须自己决定用指针还是引用，并想清楚所有权。
3. **拷贝有代价**：对象很大（如一个几千项的 `QVector`）时，一次无意的拷贝就是一次昂贵的内存复制。所以 C++ 高频用 `const T&` 入参来**避免拷贝**（见第 4 节）。
4. **QObject 例外——禁止拷贝**：`MainWindow` 头文件里的 `Q_DISABLE_COPY(MainWindow)` 直接从编译器层面禁止拷贝。为什么？QObject 有身份（是某个对象树里的节点、连着一堆信号槽），复制没有意义。C# 里引用随便赋值，Qt 直接编译期拒绝——这是"语义上不能照搬"的硬例子。

**在 DataScope 里的落地**

- `mainwindow.h` 第 34 行 `Q_DISABLE_COPY(MainWindow)`：QObject 不允许复制。
- 规范第 6 节："栈对象优先"——能用值就用值，只有需要跨函数存活/共享才升级为堆 + 智能指针。

---

## 3. 指针 vs 引用 vs 智能指针

> 可类比程度：★★★☆☆　（C# 只有一种"安全引用"；C++ 拆成五种，每种都要回答"谁拥有它"）

**一句话类比**：C# 把"指向别人"这件事只做了一种，且由 GC 保证安全；C++ 把它拆成了裸指针、引用、三种智能指针——**先回答"谁拥有这个对象"再用对工具**。

**代码对比**

```csharp
// C#：你几乎不接触裸指针，引用由 GC 托管
SomeService svc = new SomeService();
svc.Start();
```

```cpp
SomeService svc;                          // 栈对象：根本不需要指针
auto p = std::make_unique<SomeService>(); // 独占所有权：唯一 owner，析构自动 delete
auto s = std::make_shared<SomeService>(); // 共享所有权：引用计数，最后一个释放
std::weak_ptr<SomeService> w = s;         // 弱引用：不增计数，lock() 后判空再用
SomeService *raw = p.get();               // 裸指针：只"看"不"管"，生命周期不归它
```

**机制讲解**

| 工具 | 是什么 | 生命周期 | 能否为空 |
|------|--------|----------|----------|
| 裸指针 `T*` | 一个地址 | 不管，你必须自己管 | 可空（`nullptr`） |
| 引用 `T&` | 对象的别名 | 不管，绑定的对象由别人管 | 不能为空，不能改绑 |
| `std::unique_ptr<T>` | 独占指针 | owner 析构即 delete，不可拷贝只能 move | 可空 |
| `std::shared_ptr<T>` | 共享指针 | 引用计数归零时 delete，可拷贝 | 可空 |
| `std::weak_ptr<T>` | 旁观者 | 不增加计数，`lock()` 才拿 `shared_ptr` | 可空 |

**为什么不能简单照搬**

1. **每次 `new` 都要决定所有权**：栈？`unique_ptr`？`shared_ptr`？还是 Qt 的 parent 对象树？决定错了要么泄漏、要么 double-free、要么悬垂。
2. **智能指针不是免费的**：`shared_ptr` 有原子引用计数开销；`unique_ptr` 无额外开销（裸指针大小）。别把智能指针当万能胶。
3. **Qt 有第二套所有权体系**：QObject 父子树（`parent` 拥有 `child`，parent 析构时回收 child）。UI 控件应挂 parent 而非包 `shared_ptr`——**两套体系别混用**。规范第 6 节写得很清楚：UI 控件 → 挂 parent；线程对象 → `moveToThread` + `deleteLater()`；非 QObject 资源 → 智能指针。
4. **混用的危险**：一个对象被 `shared_ptr` 管着，同时又有一个裸指针指向它，裸指针那头随时可能悬垂。

**在 DataScope 里的落地**

- 规范第 6 节是权威：栈对象优先；必须堆时按"UI→parent / 线程→对象树+deleteLater / 非 QObject→智能指针"分流；**禁止** `new` 之后无归属且不 delete 的裸指针。

---

## 4. const 与 const 引用

> 可类比程度：★★☆☆☆　（C# 的 const 只对字段有意义；C++ 的 const 是函数签名的一部分——"接口契约"）

**一句话类比**：C# 的 `const` 是"一个不可变的常量"；C++ 的 `const` 是"一张贴在接口上的承诺书，编译器当裁判"。

**代码对比**

```csharp
// C#：方法参数没有 const 语义；传引用对象默认可以随便改
void Log(List<byte> frame) {
    frame.Clear();          // 合法！没有东西阻止你改
}
```

```cpp
// C++：const 引用入参 = 只读 + 不拷贝，想改？编译直接报错
void logFrame(const QByteArray &frame) {
    // frame.clear();       // 编译错误：不能修改 const 引用
}
```

**机制讲解**

- **C#**：`const` 是编译期常量，`readonly` 是运行时只读字段。方法形参没有"只读"的声明方式——传引用就是可改的（`ref`/对象默认），传值就是副本。接口签名里**表达不出**"这个参数我不改"。
- **C++**：`const T&` 同时表达三件事：只读、不拷贝、编译器强制。`const` 成员函数（如 `void toHexString(...) const`）声明"调用它不会修改对象状态"。这是一等公民，属于接口契约，调用方与实现方都被约束。

**为什么不能简单照搬**

1. 不写 `const` 就代表"我可能会改"。C# 习惯"函数随便收对象随便改"，照搬过来会写出没有 `const` 的接口，丢掉编译器保护，也让读代码的人无法判断意图。
2. `const T&` 兼职**省拷贝**：传大对象（`QByteArray`、大结构、容器）用 const 引用避免整个复制。这是 C++ 最高频的手法，C# 没有对应习惯。
3. 误用的代价：一个 `const` 引用传入、接收方却要求非 const → 编译失败。要么函数设计有问题，要么需要 `const_cast`（坏味道，几乎总是不该用）。

**在 DataScope 里的落地**

- `byteutils.h` 第 44 行 `toHexString(const QByteArray &data, const QString &separator = ...)`：const 引用入参——只读不拷贝。
- `readUInt16BE(const QByteArray &data, int offset, quint16 &out)`：**const 引用入参 + 非 const 引用出参**，对应 C# 的 `int.TryParse` 风格（返回值报成败、出参带结果）。文件头注释已经把这三个教学点标出来了。

---

## 5. delegate / event vs Signal / Slot（信号槽）

> 可类比程度：★★★★☆　（都是"多播回调"，思想一致；语法、断连时机、线程模型差异很大）

**一句话类比**：`event` 和信号槽是"同一种能力的两种方言"——但 C# 是"类内声明、外部 `+=` 订阅、GC 管安全"；Qt 是"任意两个 QObject 之间 `connect`、接收者析构自动断连、跨线程自动排队"。

**代码对比**

```csharp
// C#：event 是类内一个委托字段，外部用 += / -= 订阅退订
public event EventHandler<FrameArrivedArgs> DataArrived;
void OnData() => DataArrived?.Invoke(this, new FrameArrivedArgs(...)); // 类内触发
device.DataArrived += OnData;   // 外部订阅
device.DataArrived -= OnData;   // 外部退订
```

```cpp
// Qt：信号是类内声明、moc 生成实现；connect 显式连接任意两对象
// 类内（Q_OBJECT 才可用）：
signals:
    void dataArrived(const QByteArray &frame);

// 连接（函数指针式，编译期检查）：
connect(device, &Device::dataArrived,
        this, &MainWindow::onDataArrived);

// 或 lambda 槽（注意第 3 个参数 this = context 对象）：
connect(device, &Device::dataArrived, this,
        [this](const QByteArray &frame) { appendToView(frame); });
```

**机制讲解**

- **C# event**：一个类型安全的委托字段。发布者类内 `Invoke`，订阅者外部 `+=` / `-=`。多播（多个订阅者）。
- **Qt 信号槽**：信号只是一个成员函数声明（写在 `signals:` 区），由 **moc 生成实现**，用 `emit signal(...)` 触发。`connect` 把信号接到槽（成员函数 / lambda / 任意 callable / 另一个信号）。支持**多对多**：一个信号连多个槽、多个信号连一个槽、信号连信号。

**为什么不能简单照搬**

1. **生命周期差异（最重要的坑）**：
   - C# 的 event 泄漏方向：**发布者**通过 event 字段持有了**订阅者**的引用。订阅者本应被回收，但发布者不退订，订阅者就永远可达 → 经典事件泄漏。
   - Qt 的 QObject 析构时**自动从所有 sender 上断开自己**：接收者死了，连接就没了。这一半是 Qt 更安全。
   - 但 Qt 也有自己的坑：lambda 槽捕获裸 `this` 时，如果 `connect` 漏传 context 对象（第 3 个参数），接收者析构后 sender 仍会调用 lambda → **悬垂**。所以规范第 5 节强调：lambda 捕获 this 时注意 receiver 生命周期。
2. **触发语法不同**：C# 类内 `Event?.Invoke(...)`；Qt 用 `emit signal(...)`，`emit` 只是语法糖（最终就是一个普通函数调用）。
3. **线程模型不同**：C# event 默认同步同线程（`Invoke` 即调用）。Qt 连接默认 **AutoConnection**：同线程直连、跨线程自动转 QueuedConnection 排队投递——**信号槽天然支持跨线程**。这是 Qt 的杀手锏，规范第 7 节明确要求跨线程投递 UI 更新走信号槽。
4. **不是所有类都能发信号**：只有 **QObject 子类 + Q_OBJECT** 才有信号。C# 任何类都能定义 event。

**在 DataScope 里的落地**

- 规范第 5 节：连接优先函数指针式（编译期检查）；lambda 槽捕获 this 注意生命周期；跨线程一律依赖 AutoConnection；对象析构自动断连，需要显式时用 `disconnect`。

---

## 6. async/await vs 事件循环 + 信号槽

> 可类比程度：★★☆☆☆　（都解决"异步不阻塞"，但一个是语言级语法糖，一个是事件驱动回调——编程模型根本不同）

**一句话类比**：`async/await` 是"把异步写成同步的样子，编译器帮你生成状态机"；Qt 是"发起操作 → 注册回调 → 事件循环稍后叫你"，函数先返回，没有暂停/恢复。

**代码对比**

```csharp
// C#：await 之后代码"看起来"还是线性的，编译器生成状态机
var data = await _client.ReadAsync(addr, 8);
Process(data);              // 恢复时自动回来，局部变量还在
```

```cpp
// Qt：没有 await。发起异步操作，把"完成后做什么"装进回调
auto *reply = m_net.get(url);
connect(reply, &QNetworkReply::finished, this,
        [this, reply] {
            const auto data = reply->readAll();  // 事件循环某时刻调用这里
            Process(data);
            reply->deleteLater();
        });
// 这一行执行完，函数就返回了，事件循环接管一切
```

**机制讲解**

- **C# async/await**：编译器把方法改写成**状态机**。`await` 处挂起，I/O 完成后通过同步上下文/线程池恢复执行，后续代码"还在原函数里"，局部变量保留。这是**语言级语法糖**。
- **Qt**：`app.exec()` 就是一个 `while(true)` 事件循环，不断"取事件→分发事件"。信号槽、定时器、网络事件都由它推动。你要把"异步完成后做什么"写进槽/lambda，发起异步的那个函数**早就返回了**。没有挂起/恢复，只有回调。

**为什么不能简单照搬**

1. **代码组织从"顺序"变成"回调"**：C# 里 `await` 之后的代码还是原函数的延续；Qt 里后续逻辑必须拆进 lambda/槽，需要的数据得通过**捕获列表**带进去。逻辑稍长就会"回调嵌套"，要靠拆函数/状态机控制可读性。
2. **阻塞即死**：C# 里 UI 线程做耗时操作会卡界面，但有 async/await 轻松规避。Qt 里如果在 UI 线程的槽里 `Sleep` 或死循环，**整个事件循环卡死**，所有窗口冻结、定时器停摆。耗时操作必须丢工作线程（`QtConcurrent`/`QThread`），结果用信号槽投回 UI。
3. **错误处理**：C# 的 `await` 可以 `try/catch` 捕获异步异常；Qt 回调没有跨回调的异常——错误要么在回调里自己处理，要么走错误信号（如 `QNetworkReply::errorOccurred`）。
4. 现代 Qt 有 `QtConcurrent::run(...).then(...)` 能近似 async/await 的链式写法，但底层仍是回调，不是语言级。

**在 DataScope 里的落地**

- `main.cpp` 第 37 行 `return app.exec();`：进入事件循环，阻塞直到所有窗口关闭——这就是 WPF `Application.Run()` 的等价物。P1 总结里已经点明："Qt 的一切'看似同时发生'都是这个循环在推动。"

---

## 7. LINQ vs STL 算法 + lambda

> 可类比程度：★★★☆☆　（能力对等，都是"筛选/变换/排序"，但 LINQ 是流式链、STL 是迭代器区间）

**一句话类比**：LINQ 是"一根水管流到底"，STL 是"一个一个工位处理"——最后效果一样，中间过程不是一个思路。

**代码对比**

```csharp
// C#：链式 + 惰性求值
var highChannels = channels
    .Where(c => c.Enabled)
    .Select(c => c.Value * 2)
    .ToList();
```

```cpp
// C++：迭代器区间 + 显式算法，没有内置链式
std::vector<double> out;
std::copy_if(channels.begin(), channels.end(), std::back_inserter(out),
             [](const Channel &c) { return c.enabled; });   // Where
std::transform(out.begin(), out.end(), out.begin(),
               [](double v) { return v * 2; });             // Select
```

**机制讲解**

- **LINQ**：`IEnumerable` 惰性求值 + 扩展方法链（`Where`→`Select`→`OrderBy`→…）。一个管道流式处理，直到 `ToList()`/`foreach` 才真正执行。
- **STL**：算法操作**迭代器区间**（`begin(), end()`）。`std::copy_if`、`std::transform`、`std::sort`、`std::accumulate`。没有内建链式——要么嵌套调用，要么多个中间变量，要么写循环。两者都重度使用 lambda。

**为什么不能简单照搬**

1. **没有链式**：STL 算法不返回"可以继续 `.` 链下去"的序列。想要 `.Where().Select()` 得自己封装或写多个中间变量。
2. **迭代器心智**：STL 永远是一对 `begin/end`。空容器、边界条件都是迭代器的活；C# 的 `foreach` 把它们藏起来了。
3. **惰性 vs 立即**：LINQ 延迟执行到消费点；STL 每个算法**立即**跑完整段区间。C++17 没有内置惰性流（range 是 C++20 才有）。
4. **lambda 捕获差异**：C# lambda 自动捕获上下文；C++ 必须显式写捕获列表 `[&]`（按引用）/`[=]`（按值）/`[this]`/`[x]`。写错捕获——比如默认按值捕获一个大对象——有性能问题；捕获裸指针有生命周期问题（见第 13 节错误案例 3）。
5. 在 Qt 项目里，操作 `QVector`/`QStringList` 时直接写 range-for 循环往往更可读，别为了"用了 STL"而硬用。

---

## 8. 泛型 vs 模板（template）

> 可类比程度：★★★☆☆　（尖括号长得像；但 C# 泛型是"类型参数"，C++ 模板是"代码生成蓝图"）

**一句话类比**：C# 泛型像"一种可以填类型参数的运行时类型"；C++ 模板像"印刷机"——每次填不同的 T，就当场印出一种新类型。

**代码对比**

```csharp
// C#：运行时泛型，可反射，where 约束
T Max<T>(T a, T b) where T : IComparable<T>
    => a.CompareTo(b) > 0 ? a : b;
```

```cpp
// C++：编译期模板，用到哪个 T 就实例化出哪个版本的代码
template <typename T>
T maxOf(const T &a, const T &b) { return a > b ? a : b; }
```

**机制讲解**

- **C# 泛型**：类型参数在**运行时**仍保留（可以 `typeof(T)`、可以反射），JIT 为不同 T 生成专门或共享的代码。约束用 `where T : IFoo`、`where T : class`。
- **C++ 模板**：**编译期**展开。`template<typename T>` 只是"模式"，直到代码里出现 `maxOf<int>(1, 2)` 才实例化出 `int` 版本。约束用 `static_assert` / `std::enable_if`（C++20 用 concept）。

**为什么不能简单照搬**

1. **报错信息是劝退主力**：C# 泛型约束写错，编译器一句话讲清。C++ 模板实例化报错是一大坨"从调用点追溯到所有中间实例化"的链，新手常被吓到。解法：把模板写短、少嵌套、学会从报错尾巴往前读。
2. **特化**：C++ 模板支持全特化/偏特化——为特定 T 写专门实现（如 `vector<bool>` 特化）。C# 泛型没有这个能力。
3. **必须住在头文件**：C++ 模板的声明和实现必须同时可见（通常在 `.h` 里），不能 `.h` 声明 + `.cpp` 实现。C# 泛型类和普通类一样随便拆文件。
4. **函数重载与模板共存**：C++ 里同名模板和重载函数参与同一套重载决议，行为比 C# 复杂。

---

## 9. interface vs 抽象基类 + 虚函数

> 可类比程度：★★★★☆　（都是"定义契约 + 多态"，但 C++ 要自己处理虚析构与多继承）

**一句话类比**：`interface` 和抽象基类是同一件事——"我不管你怎么做，我只要你能做这些"。但 C++ 多送你两个必须会的技能：**虚析构** 和 **多继承**。

**代码对比**

```csharp
// C#：interface 是纯抽象契约，成员默认 public
public interface IDevice { void Start(); void Stop(); }
public class SimDevice : IDevice {
    public void Start() { /* ... */ }
    public void Stop()  { /* ... */ }
}
```

```cpp
// C++：抽象基类 = 至少一个纯虚函数
class IDevice {
public:
    virtual void start() = 0;          // = 0 表示"纯虚"，本类不可实例化
    virtual void stop()  = 0;
    virtual ~IDevice() = default;      // 黄金规则：基类析构必须 virtual！
};
```

**机制讲解**

- **C#**：`interface` 是纯抽象、无状态，一个类可实现多个接口；类继承仍单继承。调用是虚分发（virtual dispatch）。
- **C++**：抽象基类含纯虚函数（`= 0`），不能实例化。派生类 `override` 实现。C++ 支持**多继承**，可同时继承多个抽象基类（效果近似 C# 多接口）。

**为什么不能简单照搬**

1. **虚析构必须写（黄金规则）**：C++ 里如果基类析构不是 `virtual`，那么 `delete p`（`p` 是基类指针）只调用基类析构、**不调用派生类析构** → 派生类资源泄漏、未定义行为。C# 的 interface 没有析构问题（GC 管）。**凡是你想当接口/基类用的 C++ 类，析构函数一定要写 `virtual`**。
2. **访问级别要自己写**：C# interface 成员天然 `public`；C++ 要手动写 `public:` 区段，不然默认 `private`，全部白搭。
3. **override 关键字**：C++ 派生类建议写 `override`，编译器帮你核对签名是否真的覆盖了基类虚函数，防止"想重写结果写成了新函数"。
4. **多继承的菱形问题**：C++ 多继承存在菱形继承（A 被 B、C 继承，D 再继承 B、C），需要 `virtual` 继承处理；C# 接口多实现无状态，没有这个问题。
5. **Qt 的"接口"**：Qt 提供 `Q_DECLARE_INTERFACE` + `Q_INTERFACES`，配合 `qobject_cast<IFoo*>(qobject)` 实现"在 QObject 上按接口取回实现"，类似 C# 的 `as` 转换。这是 Qt 插件体系的地基，后面 P 阶段用到再深入。

---

## 10. string / List<T> / Dictionary vs QString / QVector / QMap·QHash

> 可类比程度：★★★★☆　（几乎一一对应，字典式平移；但每个都有细节差异，且 C++ 容器存的是值副本）

**一句话类比**：这就是一份"查表字典"——`string→QString`、`List<T>→QVector`、`Dictionary→QHash/QMap`。照抄名字没问题，**别照抄用法**。

**代码对比**

```csharp
string name = "ch0";                            // 不可变 UTF-16
var list = new List<double>();                  // 动态数组
var map  = new Dictionary<string, double>();    // 哈希表（无序）
```

```cpp
QString name = QStringLiteral("ch0");           // 不可变、隐式共享、UTF-16
QVector<double> values;                         // 动态数组（Qt5 的 List<T> 等价物）
QMap<QString, double> chMap;                    // 有序红黑树（按键排序）
QHash<QString, double> fastMap;                 // 无序哈希表（快，对应 Dictionary）
```

**机制讲解**

| C# | Qt/C++ | 差异要点 |
|----|--------|----------|
| `string` | `QString` | 都不可变；QString 是 UTF-16，`arg()` 格式化 |
| `List<T>` | `QVector<T>`（Qt5） / `QList<T>`（Qt6） | 都是动态数组；C++ 存值副本 |
| `Dictionary<K,V>` | `QHash<K,V>`（无序、快） / `QMap<K,V>`（有序、较慢） | C# Dictionary 是无序哈希表 → 更像 QHash |

**为什么不能简单照搬**

1. **`QString` ≠ `std::string`**：`QString` 是 UTF-16，`std::string` 是字节串。混用要显式转换（`QString::fromStdString(s)`、`.toStdString()`）。**Qt 工程里统一用 `QString`**，边界处再转换。
2. **格式化用 `arg()`**：`QString("%1 通道 = %2 V").arg(ch, value)`，对应 C# 的 `string.Format("{0}", ...)` / `$""`。注意 `%1` 不是 `{0}`，且要链式 `.arg()`。
3. **容器存值是副本**：`vec.push_back(obj)` 存 obj 的拷贝；想共享就存指针/智能指针，并明确所有权（回顾第 2、3 节）。
4. **遍历语法**：`for (double v : values)` 对应 `foreach (var v in list)`，概念一样；`QMap` 按 key 有序遍历，`QHash` 无序。
5. **`QStringLiteral` / `tr()`**：源码里的中文字面量建议包 `QStringLiteral("工业数据")`，编译期转 UTF-16、避免运行时转换；UI 上要可翻译的文本用 `tr("...")`。`mainwindow.cpp` 第 14 行 `tr("DataScope Studio —— ...")` 就是规范示例。

---

## 11. namespace 与 using

> 可类比程度：★★★★☆　（概念几乎一样；但 C++ 的 using 有"污染全局"的硬规矩，C# 没有）

**一句话类比**：`namespace` 和 C# 命名空间是一回事——都是"给符号分组避免重名"。差别在 `using`：C# 的 using 只影响当前文件，C++ 的 using 可能污染整个工程。

**代码对比**

```csharp
// C#：命名空间 + using
namespace DataScope.Utils { public class ByteUtils { /* ... */ } }
using DataScope.Utils;      // 之后直接用 ByteUtils
```

```cpp
// C++：嵌套命名空间 + using
namespace datascope { namespace utils {
    class ByteUtils { /* ... */ };
}}
using namespace datascope::utils;   // 引入整个命名空间（只在 .cpp 里用）
using datascope::utils::ByteUtils;  // 只引入一个名字（推荐，头文件里用这个）
using IntVec = std::vector<int>;    // 别名（替代 typedef）
```

**机制讲解**

- **C#**：命名空间是类型全名的一部分，编译器用 `using` 解析；程序集级别组织。
- **C++**：命名空间是纯粹的**符号作用域**：可嵌套（`datascope::utils`）、可匿名（`namespace { ... }` = 本文件私有）、可跨文件展开（多个文件写同名 namespace 自动合并）。
- using 三种形态：`using namespace X`（引入全部）、`using X::Y`（引入一个）、`using 别名 = 类型`（类型别名）。

**为什么不能简单照搬**

1. **头文件里禁止 `using namespace`（硬规矩）**：C++ 的 `#include` 是文本粘贴。头文件里写 `using namespace std;`，所有 include 它的文件都会被污染，轻则名字冲突、重则编译失败。C# 的 `using` 只作用于当前文件，没这个风险。
2. **污染范围**：`.cpp` 文件里可以谨慎用 `using namespace`；**头文件里只能用完全限定名**（`datascope::utils::ByteUtils`）或 `using 单名`。
3. **ADL（实参相关查找）**：C++ 在某些调用场景下即使没有 `using` 也能自动找到命名空间里的函数（参数就在那个命名空间里）。C# 没有这个机制，容易让人困惑"它为什么能找到？"
4. **匿名命名空间 = internal**：C++ 的 `namespace { ... }` 表示"仅本编译单元可见"，对应 C# 的 `internal`。C++ 没有"命名空间级访问修饰符"，用匿名命名空间补位。

**在 DataScope 里的落地**

- `byteutils.h` 用 `namespace datascope { namespace utils { ... } }`，对应 C# 的 `DataScope.Utils`；类全名是 `datascope::utils::ByteUtils`。这是规范里的标准组织方式。

---

## 12. struct 在 C++ 与 C# 的差异

> 可类比程度：★★☆☆☆　（同名不同物：C# 的 struct 是"值类型分界"，C++ 的 struct 和 class 只是"默认访问级别"不同）

**一句话类比**：C# 里 `struct` 是"轻量值类型"，`class` 是"引用类型"——这是**重大语义抉择**；C++ 里 `struct` 和 `class` 几乎是同义词，只差一个默认访问级别。同名，但承载的决策完全不同。

**代码对比**

```csharp
// C#：struct = 值类型（拷贝语义、可装箱、不能继承类）
struct Point { public int X; public int Y; }
var p1 = new Point { X = 1, Y = 2 };
var p2 = p1;          // 值拷贝，p2 独立
```

```cpp
// C++：struct 与 class 等价，默认 public
struct Point { int x; int y; };   // 默认 public，省去 public:
```

**机制讲解**

- **C#**：`struct` 是值类型——赋值拷贝、常在栈上（装箱才上堆）、**不能继承**（只能实现接口）。选择 struct 还是 class = 选择"值语义"还是"引用语义"，影响别名行为与性能。
- **C++**：`struct` 与 `class` **完全等价**，唯一区别是默认访问级别（struct 默认 `public`，class 默认 `private`）。C++ struct 可以构造析构、继承、虚函数、多态——**C# struct 做不到的它全能做**。

**为什么不能简单照搬**

1. **语义抉择不存在**：C# 里选 struct/class 是"值 vs 引用"的抉择；C++ 里一切对象都是值语义，struct/class 只是风格。别试图在 C++ 里复刻 C# 的"struct 就该小而轻"——C++ struct 完全可以当 C# class 用。
2. **默认访问级别陷阱**：C++ struct 默认 `public`。从 C# 带过来"成员默认私有"的直觉，可能在 struct 里意外公开字段；反过来，用 `class` 时又容易忘记写 `public:` 导致全部私有。
3. **POD / 与 C 互操作**：C++ 的 struct 常用于"数据聚合"（POD，plain old data），内存布局与 C 结构体兼容，直接用于协议解析、内存映射。这对应 C# 里 `[StructLayout]` 的 struct，但 C++ 更原生、更常用。

---

## 13. 错误案例：把 C# 思维直接搬进 Qt

这一节是全文的"反面教材合集"。每一个都是 C# 开发者第一次写 Qt 时最常见的真实事故。请反复对照第 1~12 节理解根因。

### 错误案例 1：到处 `new` 后忘记管理生命周期

**C# 惯性**：`new` 完交给 GC，放心。

```csharp
var timer = new System.Timers.Timer();  // GC 会收，不用管
timer.Start();
```

**Qt 照搬**：

```cpp
auto *timer = new QTimer;   // 没有 parent！没有智能指针！没有人 delete！
timer->start(1000);
```

- **现象**：程序运行越久内存越涨；窗口关闭后资源不释放。
- **根因**：C# 有 GC 兜底，C++ 裸 `new` 必须有人负责 `delete`。这个 `QTimer` 无父无主，成了"孤儿对象"，谁都不管它。
- **正确写法**：挂对象树（parent 负责回收）或直接栈对象：

```cpp
auto *timer = new QTimer(this);   // 挂到 this 的对象树，随 this 析构
// 或者：作用域内够用就上栈
QTimer timer;
timer.start(1000);
```

- **对照**：第 1 节 GC vs RAII、第 3 节所有权。

### 错误案例 2：以为信号槽像 event 一样"只在同类中使用"

**C# 惯性**：event 在定义它的类里 `Invoke`，外部订阅，很少想"跨对象手动桥接"。

**Qt 照搬**：写了个槽函数，期望"信号会自动调到自己身上"：

```cpp
// 槽写了，但从来没 connect —— 信号根本找不到它
class MainWindow {
    void onDataArrived(const QByteArray &frame);  // 只是个普通成员函数
};
```

- **现象**：界面毫无反应，断点打在 `onDataArrived` 里就是不进。
- **根因**：Qt 的信号槽**必须显式 `connect`** 才能建立连接。信号可以来自任何 QObject，槽可以是任何 QObject 的成员函数/lambda。它**不是** C# 那种"类内一个 event 字段 + 外部 += "的心智。
- **正确写法**：

```cpp
connect(device, &Device::dataArrived,
        this, &MainWindow::onDataArrived);
```

- **注意**：sender、receiver 都必须是 QObject 子类；漏了 `connect` 编译器不报错，只是"没反应"——所以**每次写完信号槽先自问：connect 了吗？**
- **对照**：第 5 节信号槽。

### 错误案例 3：在 lambda 里捕获裸 `this` 导致悬垂

**C# 惯性**：lambda 捕获 this 由 GC 保证安全。

```csharp
btn.Click += (s, e) => Process();   // this 一定活着，GC 保证
```

**Qt 照搬**：

```cpp
// 危险！connect 漏了第 3 个参数（context 对象）
connect(btn, &QPushButton::clicked, [this] { process(); });
```

- **现象**：`this`（接收者）析构之后，`btn` 还活着，用户再点一次 → 崩溃。这类崩溃"时好时坏"，是最难查的悬垂指针。
- **根因**：connect 没传 context 对象时，连接**不随接收者析构而断开**；lambda 捕获了裸 `this`，却没人保证 `this` 还活着。Qt5 的函数指针式 connect 在 receiver 是 QObject 时能自动断开，但**裸 lambda 没有这个保护**。
- **正确写法**：**必须传 context 对象（第 3 个参数）**：

```cpp
connect(btn, &QPushButton::clicked, this,
        [this] { process(); });   // 传了 this，Qt 在 this 析构时自动断连
```

- **补充**：如果 `this` 不是 QObject，就改用 `QPointer<T>` 或 `std::weak_ptr`，回调里先判空再用。
- **对照**：第 5 节生命周期差异；这正是规范第 5 节"lambda 槽捕获 this 时注意 receiver 生命周期"所指。

### 错误案例 4（补充）：把对象塞进容器，以为存的是引用

```cpp
QVector<Channel> channels;
Channel c;
channels.push_back(c);   // 副本！容器里是 c 的拷贝
c.name = QStringLiteral("ch1");  // 改的是原对象，容器里那份纹丝不动
```

- **现象**：改完原对象，遍历容器发现数据没变。
- **根因**：C++ 容器存值副本（第 2、10 节）。C# 的 `list.Add(c)` 存引用。
- **正确**：想共享，用 `QVector<Channel*>`（不拥有）或智能指针容器（拥有），并明确所有权。

### 错误案例 5（补充）：QString 与 std::string 混用

```cpp
std::string s = "abc";
label->setText(s);   // 编译报错：setText 要 QString
QString q = s;       // 也不是隐式转换；强转还可能乱码
```

- **根因**：`QString`（UTF-16）与 `std::string`（字节串）是两种东西（第 10 节）。
- **正确**：Qt 工程统一 `QString`；需要边界转换用 `QString::fromStdString(s)` / `.toStdString()`。

---

## 14. 对照速查表（基础篇）

| 概念 | 类比程度 | 一句话差异 |
|------|----------|-----------|
| GC vs RAII | ★★☆☆☆ | 定时回收 vs 作用域到点即释放 |
| 引用类型 vs 值语义 | ★★★☆☆ | C# 变量是遥控器，C++ 变量是电视机本身 |
| 指针/引用/智能指针 | ★★★☆☆ | 先回答"谁拥有它"，再选工具 |
| const / const 引用 | ★★☆☆☆ | C++ 的 const 是接口契约，兼做省拷贝 |
| event vs 信号槽 | ★★★★☆ | 都要显式 connect/订阅；生命周期与线程模型不同 |
| async/await vs 事件循环 | ★★☆☆☆ | 语言级语法糖 vs 事件驱动回调 |
| LINQ vs STL 算法 | ★★★☆☆ | 流式链 vs 迭代器区间；无内置链式 |
| 泛型 vs 模板 | ★★★☆☆ | 运行时类型参数 vs 编译期代码生成蓝图 |
| interface vs 抽象基类 | ★★★★☆ | C++ 要写虚析构、处理多继承 |
| string/List/Dictionary | ★★★★☆ | 字典式对应；C++ 容器存值副本 |
| namespace / using | ★★★★☆ | 头文件里禁止 using namespace |
| struct | ★★☆☆☆ | 同名不同物：C# 分值是引用，C++ 只分默认访问级别 |

---

## 15. 练习题（请你亲自做）

1. 用栈对象 + `const T&` 入参，写一个 `isFrameValid(const QByteArray &frame)` 函数，并在 `byteutils.h` 同目录新建一个测试用的小函数。
2. 在 `mainwindow.cpp` 里 `connect` 一个按钮信号到 lambda，故意漏掉第 3 个 context 参数，编译运行后写一句注释说明风险（然后补上）。
3. 把 C# 的 `list.Where(x => x > 0).Select(x => x * 2).ToList()` 用 STL 的 `copy_if` + `transform` 改写一遍。
4. 解释：为什么 C++ 抽象基类必须有虚析构？如果不写会出什么问题？
5. 用 `QMap` 和 `QHash` 各存一份配置，说出什么场景选哪个、为什么。
6. 在 `mainwindow.cpp` 的 `#include` 段加一个命名空间混用的错误示例（`using namespace std;` 放在头文件），观察编译结果，理解"污染"。

---

## 附录 A：本文与工程文档的关系

| 参考来源 | 用途 |
|----------|------|
| `docs/stages/P01.md` | 阶段总结格式、WPF 对照表写法、错误案例写法 |
| `docs/05-开发规范/开发规范.md` | 命名、内存管理、信号槽、线程安全规范（本文多处引用） |
| `DataScope/src/app/main.cpp` | 栈上窗口、事件循环、`app.exec()` 的真实代码 |
| `DataScope/src/ui/mainwindow.h/.cpp` | `Q_OBJECT`、`Q_DISABLE_COPY`、`tr()` 的真实代码 |
| `DataScope/src/utils/byteutils.h` | const 引用入参、非 const 引用出参、嵌套命名空间的真实代码 |

> 后续：`docs/04-WPF转Qt对照指南/` 将随阶段补充「UI 布局与控件」「事件循环与线程」「模型/视图」「网络与协议」「调试与构建」等分册。
