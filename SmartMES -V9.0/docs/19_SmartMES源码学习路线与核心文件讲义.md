# SmartMES 源码学习路线与核心文件讲义

> 文档定位：源码学习路线图 + 讲义型文档  
> 目标：帮助读者不是“乱看代码”，而是沿着正确顺序，从项目结构、核心模块、关键文件、工业逻辑、架构思想几个层面，把 `SmartMES` 真正读懂。  
> 适用对象：希望从源码中学习工业上位机系统设计的人。

---

# 1. 为什么要有“源码学习路线”？

很多人看项目源码时有一个常见问题：

- 打开项目
- 随便点一个文件
- 看了几页代码
- 发现看不懂
- 然后开始怀疑自己

其实大多数时候不是你能力不够，而是**阅读顺序错了**。

工业上位机项目和普通小项目不同，它通常同时包含：

- UI
- 通信
- 控制
- 数据
- 流程
- 状态机
- 安全
- 追溯
- 日志
- 权限

如果没有阅读路线，很容易在细节里迷路。

所以这份文档的目标是：

> 告诉你按什么顺序看、每个关键文件看什么、从中学什么。

---

# 2. 学源码之前，你脑子里要先有三张地图

在真正读代码前，你先记住这三张“认知地图”。

## 地图 1：项目分层地图

```text
UI -> Services -> Modules -> Core -> Native
```

意思是：
- UI 负责展示和交互
- Services 负责公共系统能力
- Modules 负责业务功能
- Core 负责底层工业能力
- Native 负责原生扩展

## 地图 2：功能域地图

- 导航与界面
- 通信
- 设备控制
- 自动化流程
- 数据管理
- 视觉检测
- MES 协同
- 安全与权限
- 日志与报警
- 调度、状态机、追溯、配方

## 地图 3：学习顺序地图

正确顺序应是：

1. 先看系统怎么组织页面
2. 再看系统怎么组织状态和公共能力
3. 再看具体业务模块
4. 最后看测试、原生扩展和工程化细节

---

# 3. 推荐总学习顺序

我建议你按 6 个阶段读。

## 阶段 1：先搞懂“项目整体长什么样”

先看：
- `README.md`
- `docs/12_项目认知文档.md`
- `docs/13_项目目录逐层解读.md`
- `docs/17_从SmartMES出发理解工业上位机_完整功能知识文档.md`
- `docs/18_工业上位机核心模块逐项精讲.md`

### 这个阶段你要解决的问题
- 这是什么项目？
- 为什么它不是普通桌面软件？
- 它有哪几层？
- 它的大功能块是什么？

### 学完后你应该能回答
- `SmartMES` 是工业上位机样板
- 它不是只做 UI，而是有工业内核
- 它有完整的分层结构

---

# 4. 阶段 2：从主入口理解“系统怎么组织给用户”

这一阶段最重要的是理解：

> 系统是怎么把这么多模块组织成一个完整软件的。

## 4.1 第一优先文件：`MainViewModel.cs`

路径：
- `SmartMES.UI/ViewModels/MainViewModel.cs`

### 为什么第一个看它？
因为它是全系统的“导航大脑”。

### 看它时重点看什么？

#### 1）有哪些页面 ViewModel 被统一管理
你会看到：
- Dashboard
- Device
- Alarm
- Log
- Settings
- User
- Communication
- Automation
- MesComm
- File
- Database
- Motion
- Native
- Motion10
- Vision
- VisionMotion
- Industrial

这能帮你一次性看清整个系统有哪些主要页面。

#### 2）导航命令是怎么组织的
你会看到一大批 `NavXxxCommand`。

这说明：
- 页面切换不是页面自己乱跳
- 而是被统一管理

#### 3）权限逻辑怎么作用在导航上
你会看到：
- `CanAccessDashboard`
- `CanAccessMotion`
- `CanAccessVision`
- `EnsureCurrentPageAccessible()`

这说明权限不是停留在“用户数据”层，而是真正作用到 UI 行为上了。

### 你从这个文件应该学到什么？

- 主导航协调器的作用
- 如何统一管理页面实例
- 如何把权限和导航绑在一起
- 工业项目为什么需要一个“总控型 ViewModel”

---

## 4.2 第二优先文件：`MainWindow.xaml`

路径：
- `SmartMES.UI/Views/MainWindow.xaml`

### 看它时重点看什么？

- 左侧导航如何组织
- 中间内容区如何切换
- DataTemplate 如何把 ViewModel 映射到 View
- 页面布局如何表达工业系统结构

### 学习重点

> 主窗口不是一个普通窗口，而是整个系统的“总壳”。

---

# 5. 阶段 3：先学工业基础能力，再学具体业务功能

这一阶段非常重要。很多人会直接冲进运动控制或视觉代码，但更建议你先读 `Core`。

因为：

> `Core` 决定这个项目为什么像工业软件，而不是普通功能集合。

---

## 5.1 `ViewModelBase.cs`

路径：
- `SmartMES.Core/Infrastructure/ViewModelBase.cs`

### 重点看什么
- 属性通知机制
- `SetProperty`
- `OnPropertyChanged`

### 你会学到什么
- MVVM 为什么成立
- UI 为什么能自动刷新
- ViewModel 为什么是 WPF 项目的核心骨架之一

---

## 5.2 `RelayCommand.cs`

路径：
- `SmartMES.Core/Infrastructure/RelayCommand.cs`

### 重点看什么
- 命令如何执行
- 命令是否可执行
- 按钮状态为什么会变化

### 你会学到什么
- 工业 UI 的按钮不是“直接绑定事件”，而是绑定命令
- 命令机制为什么适合做权限控制和状态控制

---

## 5.3 `StateMachineEngine.cs`

路径：
- `SmartMES.Core/StateMachine/StateMachineEngine.cs`

### 这是为什么必须重点看？
因为它代表工业项目的“状态思维”。

### 重点看什么
- `MachineState`
- `StateTransition`
- `Fire()`
- `BuildStandard()`
- `Guard`
- `StateChanged`

### 学习重点

你要问自己：
- 为什么工业设备需要标准状态？
- 为什么状态不能乱跳？
- 为什么要有守卫条件？
- 为什么状态变化要发事件？

### 你应该学到什么

> 工业软件核心之一，就是把设备行为从“散乱逻辑”变成“可控状态流转”。

---

## 5.4 `TaskSchedulerService.cs`

路径：
- `SmartMES.Core/Scheduler/TaskSchedulerService.cs`

### 为什么重要？
它代表“后台任务组织能力”。

### 重点看什么
- `ScheduledTask`
- `TaskPriority`
- `AddTask`
- `StartAsync`
- `ExecuteTaskAsync`

### 学习重点

问自己：
- 为什么工业系统需要统一调度？
- 为什么任务有优先级？
- 为什么需要 `Pending / Running / Completed / Faulted` 状态？

### 你应该学到什么

> 系统里的后台逻辑，不应该散落各处，而应该进入统一调度体系。

---

## 5.5 `SafetyService.cs`

路径：
- `SmartMES.Core/Safety/SafetyService.cs`

### 为什么重要？
因为它体现工业软件最关键的“风险思维”。

### 重点看什么
- 安全条件如何判断
- 急停如何触发
- 急停如何复位
- 互锁规则如何表达

### 你应该学到什么

> 真正的控制系统，必须先考虑“不能做什么”，再考虑“能做什么”。

---

## 5.6 `RecipeService.cs`

路径：
- `SmartMES.Core/Recipe/RecipeService.cs`

### 为什么重要？
因为它体现制造业里“产品工艺参数管理”的思路。

### 重点看什么
- 配方对象是什么
- 当前活动配方如何切换
- 配方如何保存与加载

### 你应该学到什么

> 配方系统本质上是产品工艺知识的载体。

---

## 5.7 `TraceService.cs`

路径：
- `SmartMES.Core/Traceability/TraceService.cs`

### 为什么重要？
因为它代表工业项目的“可负责能力”。

### 重点看什么
- `ProductTrace`
- `ProcessRecord`
- `StartTrace`
- `StartProcess`
- `EndProcess`
- `AddParameter`

### 你应该学到什么

> 追溯不是附加功能，而是制造系统完整性的标志。

---

## 5.8 `PluginLoader.cs`

路径：
- `SmartMES.Core/Plugin/PluginLoader.cs`

### 为什么重要？
因为它代表平台化意识。

### 重点看什么
- 插件接口怎么定义
- 插件怎么加载
- 插件信息如何维护

### 你应该学到什么

> 一个工业系统如果要长期扩展，必须从一开始就考虑扩展边界。

---

# 6. 阶段 4：进入业务模块层，理解“能力如何落地”

到这一阶段，你已经知道：
- 系统怎么导航
- 工业基础能力是什么

现在你可以进入具体业务模块。

---

## 6.1 先看运动控制：`AxisController.cs`

路径：
- `SmartMES.Modules/MotionControl/AxisController.cs`

### 为什么优先看它？
因为运动控制是最典型的工业执行逻辑之一。

### 看什么
- `AxisState`
- `MoveTo`
- `Home`
- `Pause`
- `Resume`
- `Stop`
- `Reset`
- 状态切换逻辑
- 后台线程执行逻辑

### 学习重点

你要特别思考：
- 为什么移动动作不在 UI 线程执行？
- 为什么运动过程要有状态流转？
- 为什么 Stop、Error、Reset 要单独建模？

### 你应该学到什么

> 运动控制的本质不是“修改位置值”，而是“组织一个具备状态和约束的执行过程”。

---

## 6.2 再看多轴与十轴

路径：
- `SmartMES.Modules/MotionControl/MultiAxisController.cs`
- `SmartMES.Modules/MotionControl/TenAxisController.cs`

### 学什么
- 从单轴走向多轴
- 多轴协同控制怎么组织
- 程序执行为什么比单动作复杂得多

### 你会学到什么

> 工业控制一旦从单对象走向多对象，复杂度是指数上升的。

---

## 6.3 看视觉：`VisionEngine.cs`

路径：
- `SmartMES.Modules/Vision/VisionEngine.cs`

### 为什么看它？
因为它很好地展示了“检测链路”的基本形态。

### 看什么
- 图像生成
- 灰度化
- Sobel 边缘检测
- 阈值检测
- 缺陷框绘制
- 检测结果模型

### 你应该学到什么

> 视觉模块的价值，不只是算法本身，而是“采图 -> 处理 -> 判定 -> 结果输出 -> 可视化”这一整条链路。

---

## 6.4 看数据库：`DbContext.cs`

路径：
- `SmartMES.Modules/Database/DbContext.cs`

### 看什么
- 生产记录实体
- 设备日志实体
- 多数据库适配
- `DbContextFactory`

### 你应该学到什么

> 数据库模块在上位机里不是单纯存数据，而是承接系统记忆、统计和追溯能力。

---

## 6.5 看 MES 通信：`HttpMesClient.cs`

路径：
- `SmartMES.Modules/MesComm/HttpMesClient.cs`

### 看什么
- 连接建立
- 获取工单
- 上报结果
- 订阅回调
- 通信消息输出

### 你应该学到什么

> MES 客户端不是纯 HTTP 调用，而是“把现场执行结果转成上层系统能理解的业务数据”。

---

## 6.6 看原生互操作：`NativeMath.cs`

路径：
- `SmartMES.Modules/NativeInterop/NativeMath.cs`

### 看什么
- `DllImport`
- 托管与非托管边界
- 可用性判断
- 便捷封装

### 你应该学到什么

> 工业项目里，C# 不一定包打天下，很多真实系统都需要对接原生 DLL 或 C++ SDK。

---

# 7. 阶段 5：回到 Services 层，理解“公共能力如何协调全局”

很多初学者会跳过 `Services`，但其实它是系统的“中间协调层”。

---

## 7.1 `LoggingService.cs`

### 学什么
- 日志为什么不能散在每个地方
- 为什么要统一入口
- 为什么模块名和级别很重要

### 你会学到什么

> 日志服务本质上是系统级横切能力。

---

## 7.2 `EventBusService.cs`

### 学什么
- 模块为什么不应该彼此硬耦合
- 为什么要发布/订阅事件

### 你会学到什么

> 复杂系统里，事件总线是解耦利器。

---

## 7.3 `AutomationEngine.cs`

### 学什么
- 步骤化流程如何执行
- 步骤状态如何记录
- 流程失败如何中断

### 你会学到什么

> 流程引擎是上位机从“控制台”变成“执行系统”的关键桥梁。

---

# 8. 阶段 6：最后看测试工程，验证你是否真的理解了系统

很多人看源码从不看测试，这是一个损失。

因为测试告诉你：
- 作者认为什么是关键逻辑
- 系统的边界在哪
- 哪些行为是必须保证的

## 8.1 重点看这些测试

- `StateMachineEngineTests.cs`
- `TaskSchedulerServiceTests.cs`
- `SafetyServiceTests.cs`
- `RecipeServiceTests.cs`
- `TraceServiceTests.cs`
- `AxisControllerTests.cs`
- `MainViewModelPermissionTests.cs`

## 8.2 为什么测试值得看？

因为测试往往是“浓缩后的行为定义”。

比起上来啃 300 行实现代码，先看测试，你更容易知道：
- 这个类最重要的行为是什么
- 它的正确性标准是什么

---

# 9. 如果你时间有限，只看这 10 个文件

如果你现在时间有限，我建议你优先看这 10 个文件：

1. `SmartMES.UI/ViewModels/MainViewModel.cs`
2. `SmartMES.Core/Infrastructure/ViewModelBase.cs`
3. `SmartMES.Core/Infrastructure/RelayCommand.cs`
4. `SmartMES.Core/StateMachine/StateMachineEngine.cs`
5. `SmartMES.Core/Scheduler/TaskSchedulerService.cs`
6. `SmartMES.Core/Safety/SafetyService.cs`
7. `SmartMES.Core/Traceability/TraceService.cs`
8. `SmartMES.Modules/MotionControl/AxisController.cs`
9. `SmartMES.Modules/Vision/VisionEngine.cs`
10. `SmartMES.Services/Automation/AutomationEngine.cs`

这 10 个文件基本可以帮你看见这个项目最核心的骨架。

---

# 10. 学源码时，你每看一个文件都应该问自己什么？

我给你一个通用问题模板。

## 文件分析五连问

1. 这个文件解决什么问题？
2. 它属于哪一层？为什么放在这层？
3. 它对外暴露什么能力？
4. 它依赖谁？谁又依赖它？
5. 它体现了什么工业软件思维？

如果你每看一个文件都能回答这 5 个问题，你的理解会快很多。

---

# 11. 学源码时最容易犯的错误

## 错误 1：只看代码细节，不看它在系统中的位置
一个方法再复杂，如果你不知道它在整个系统中扮演什么角色，理解就会很碎。

## 错误 2：一上来啃业务模块，不先看 Core
这样你会知道“它怎么写”，却不知道“为什么要这么写”。

## 错误 3：把 UI 当核心，把工业逻辑当附属
在工业软件里，往往恰恰相反：
- UI 是表层
- 工业逻辑是内核

---

# 12. 这份源码学习路线真正想帮你建立什么能力？

不是让你“把所有文件看完”，而是让你建立：

## 12.1 结构化阅读能力
知道先看什么、后看什么。

## 12.2 模块定位能力
知道一个文件在系统里负责什么。

## 12.3 反推设计能力
能从代码反推出作者的架构思路。

## 12.4 工业软件思维
能看到状态、安全、追溯、调度这些隐藏价值。

---

# 13. 一句话总结整篇讲义

> 学 `SmartMES` 源码，最重要的不是把代码一行行看完，而是先建立系统视角：从主导航看组织方式，从 Core 看工业内核，从 Modules 看能力落地，从 Services 看全局协调，从 Tests 看行为边界；这样你学到的就不只是代码，而是工业上位机的架构与思维方式。
