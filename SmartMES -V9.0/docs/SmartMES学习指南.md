<!-- markdownlint-disable -->
# SmartMES V9.0 学习指南

> 适合人群：有 C# 基础，希望系统学习工业 MES 上位机开发的开发者

---

## 学习路线总览

```
阶段一：环境搭建与运行  （1天）
    ↓
阶段二：理解分层架构    （2天）
    ↓
阶段三：核心机制精读    （3天）
    ↓
阶段四：业务模块学习    （3天）
    ↓
阶段五：UI 与交互      （2天）
    ↓
阶段六：实战练习        （持续）
```

---

## 阶段一：环境搭建与运行

### 目标
能够在本地成功编译并运行 SmartMES，看到主界面。

### 前置要求
- 安装 [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- 安装 Visual Studio 2022 或 VS Code（推荐安装 C# Dev Kit 扩展）

### 步骤

```bash
# 1. 进入项目目录
cd "D:/软件/Cursor/文档存放/代码区/SmartMES -V9.0 - 副本"

# 2. 还原依赖包
dotnet restore SmartMES.sln

# 3. 编译整个解决方案
dotnet build SmartMES.sln

# 4. 运行 UI 项目
dotnet run --project SmartMES.UI
```

### 登录系统
启动后使用内置账号登录：
- 管理员：`admin` / `admin123`
- 操作员：`operator` / `op123`
- 只读：`viewer` / `view123`

### 验收标准
- [ ] 能看到主界面 Dashboard
- [ ] 能切换到 Device、Alarm、Log 等视图
- [ ] 能看到模拟 I/O 数据在实时变化

---

## 阶段二：理解分层架构

### 目标
理解项目的 5 层结构和单向依赖关系，建立整体认知。

### 核心概念

#### 分层架构图
```
┌─────────────────────────────┐
│       SmartMES.UI           │  ← 用户界面（WPF）
├─────────────────────────────┤
│     SmartMES.Services       │  ← 业务逻辑服务
├─────────────────────────────┤
│     SmartMES.Modules        │  ← 功能业务模块
├─────────────────────────────┤
│      SmartMES.Core          │  ← 核心接口与模型
├─────────────────────────────┤
│     SmartMES.Native         │  ← C++ 原生模块
└─────────────────────────────┘
```

**关键原则**：上层依赖下层，下层不知道上层的存在。

### 学习任务

**任务 1：读懂解决方案结构**
打开 `SmartMES.sln`，展开每个项目，观察文件夹组织方式。

**任务 2：理解接口定义**
阅读 `SmartMES.Core/Interfaces/` 下的接口文件：
- `IAlarmService` — 报警服务接口
- `ICommunicationService` — 通信服务接口
- `IDevice` — 设备抽象接口
- `IEventBus` — 事件总线接口
- `ILoggingService` — 日志服务接口
- `ISettingsService` — 配置服务接口

**任务 3：理解数据模型**
阅读 `SmartMES.Core/Models/` 下的模型类：
- `AlarmRecord.cs`
- `DeviceModel.cs`
- `LogEntry.cs`
- `SystemSettings.cs`
- `UserModel.cs`

### 思考问题
1. 为什么 Core 层只依赖 `System.Text.Json`，不依赖 EF Core？
2. 接口定义在 Core，实现在 Services/Modules，这样设计有什么好处？

---

## 阶段三：核心机制精读

### 3.1 事件总线（EventBus）

事件总线是模块间通信的核心，避免了模块之间的直接依赖。

#### 工作原理
```
模块A  →  Publish(事件)  →  EventBus  →  Subscribe(事件)  →  模块B
```

#### 内置事件类型
| 事件 | 触发时机 |
|------|----------|
| `DeviceStatusEvent` | 设备连接状态变化 |
| `NewAlarmEvent` | 新报警产生 |
| `AutomationStatusEvent` | 自动化流程步骤更新 |
| `UserLoginEvent` | 用户登录（携带角色和权限） |

#### 学习任务
找到 `IEventBus` 接口，理解 `Subscribe`、`Publish`、`Unsubscribe` 三个方法的签名，然后在代码中搜索 `Publish` 的调用位置，追踪一个事件的完整流转过程。

---

### 3.2 I/O 系统

#### 接口定义（IIoDevice）
```csharp
bool ReadInput(int address);           // 读数字输入
void WriteOutput(int address, bool value); // 写数字输出
double ReadAnalog(int address);        // 读模拟量输入
void WriteAnalog(int address, double value); // 写模拟量输出
IEnumerable<IoChannel> GetChannels();  // 获取所有通道
event EventHandler<ChannelChangedEventArgs> ChannelChanged; // 值变化通知
```

#### 地址映射
```
DI（数字输入）：地址 0-15    （16通道）
DO（数字输出）：地址 100-115 （16通道）
AI（模拟输入）：地址 200-203 （4通道）
AO（模拟输出）：地址 300-303 （4通道）
```

#### 仿真设备（SimulatedIoDevice）
无需真实硬件，系统内置仿真实现：
- DI 通道每 1-2 秒随机翻转
- AI 通道在 0-100 范围内随机变化
- 使用 `lock` 保证线程安全

#### 学习任务
阅读 `SimulatedIoDevice` 的实现，理解：
1. 如何用 `System.Threading.Timer` 驱动模拟数据
2. 如何用 `lock` 保护共享状态
3. 如何触发 `ChannelChanged` 事件通知订阅者

---

### 3.3 状态机（StateMachine）

状态机用于管理工业控制流程中的状态转换，例如：
```
空闲 → 初始化 → 运行中 → 暂停 → 运行中 → 完成
```

#### 学习任务
找到 `SmartMES.Core/StateMachine/` 目录，理解：
1. 状态（State）和转换（Transition）的定义方式
2. 如何触发状态转换
3. 状态机如何与 EventBus 配合使用

---

### 3.4 调度器（Scheduler）

调度器负责任务的定时执行和编排，类似于工业版的 Cron。

#### 学习任务
找到 `SmartMES.Core/Scheduler/` 目录，理解：
1. 任务（Task）的定义结构
2. 如何注册和启动定时任务
3. 任务执行失败时的处理机制

---

## 阶段四：业务模块学习

### 4.1 数据库模块（Database Module）

#### 两种 ORM 的使用场景
| 场景 | 推荐方案 |
|------|----------|
| 复杂查询、关联查询 | Entity Framework Core |
| 简单 SQL、高性能批量操作 | Dapper |

#### EF Core 多数据库切换
```csharp
// SQLite（开发/测试）
optionsBuilder.UseSqlite("Data Source=smartmes.db");

// SQL Server（生产）
optionsBuilder.UseSqlServer("Server=...;Database=SmartMES;...");

// MySQL（生产备选）
optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
```

#### 学习任务
1. 找到 DbContext 的定义，查看实体配置
2. 找到一个使用 Dapper 的查询，对比 EF Core 写法的差异
3. 理解 `AlarmRecord` 如何被持久化到数据库

---

### 4.2 运动控制模块

#### 3轴运动控制（MotionControl）
基础运动控制，适合简单的 XYZ 定位场景。

#### 10轴运动控制（Motion10Axis）
支持 G-code 指令，适合复杂轨迹加工：
```
G00 X100 Y200 Z50   ; 快速定位
G01 X150 Y250 F100  ; 直线插补，进给速度100
G02 X200 Y200 R50   ; 顺时针圆弧插补
```

#### 学习任务
1. 找到 G-code 解析器的实现
2. 理解轴坐标系的定义方式
3. 了解运动控制如何与安全联锁（Safety）配合

---

### 4.3 视觉模块（Vision）

视觉模块负责图像采集和缺陷检测。

#### 视觉+运动协同（VisionMotion）
```
采集图像 → 视觉检测 → 计算偏差 → 运动补偿 → 重新检测
```

#### 学习任务
1. 找到视觉检测结果的数据结构
2. 理解视觉结果如何传递给运动控制模块
3. 了解 VisionMotion 的协调调度逻辑

---

### 4.4 配方管理（Recipe）

配方（Recipe）存储了生产某种产品所需的所有工艺参数。

#### 典型配方结构
```
配方名称: 产品A_标准工艺
├── 温度: 85°C
├── 压力: 5 MPa
├── 速度: 1500 rpm
├── 时间: 120s
└── 步骤序列: [预热, 加压, 保持, 冷却]
```

#### 学习任务
找到 `SmartMES.Core/Recipe/` 目录，理解配方的加载、切换和执行流程。

---

## 阶段五：UI 与交互

### 5.1 WPF 基础回顾

如果你对 WPF 不熟悉，先掌握这几个核心概念：

| 概念 | 说明 |
|------|------|
| XAML | XML 格式的 UI 描述语言 |
| DataBinding | 数据绑定，UI 自动反映数据变化 |
| MVVM | Model-View-ViewModel 架构模式 |
| ResourceDictionary | 样式/模板的集中管理 |
| UserControl | 可复用的 UI 组件 |

### 5.2 项目中的 MVVM 实践

```
View（.xaml）     ←绑定→    ViewModel（.cs）    ←调用→    Service/Module
  界面展示                    数据与命令                    业务逻辑
```

#### 学习任务
1. 打开 `DashboardView.xaml`，找到数据绑定的写法（`{Binding ...}`）
2. 找到对应的 ViewModel，理解属性如何通知 UI 更新
3. 找到一个按钮命令（`ICommand`）的完整实现链路

### 5.3 样式系统

所有样式集中在 `SmartMES.UI/Resources/Styles.xaml`，通过 `App.xaml` 全局引入。

#### 学习任务
打开 `Styles.xaml`，了解：
1. 如何定义全局颜色主题
2. 如何为 Button、TextBox 等控件定义统一样式
3. 如何在 View 中引用这些样式

---

## 阶段六：实战练习

完成前五个阶段后，通过以下练习巩固理解：

### 练习 1：添加新报警规则（简单）
在 `settings.json` 中添加一个新的报警阈值参数，然后在 `SystemSettings.cs` 中添加对应属性，最后在报警服务中实现检测逻辑。

### 练习 2：新增一个 I/O 通道（中等）
在 `SimulatedIoDevice` 中添加一个新的模拟量输出通道（AO），并在 Dashboard 上显示其当前值。

### 练习 3：实现一个简单状态机（中等）
为"设备启动流程"实现一个状态机：
```
待机 → 自检 → 预热 → 就绪 → 运行 → 停止 → 待机
```
每个状态转换时通过 EventBus 发布状态变化事件。

### 练习 4：添加新的数据库查询（中等）
使用 Dapper 实现一个"查询最近 N 条报警记录"的接口，并在 AlarmView 中展示结果。

### 练习 5：开发一个新模块（进阶）
参考现有模块结构，开发一个"能耗监控模块"：
- Core 层：定义 `IEnergyService` 接口和 `EnergyRecord` 模型
- Modules 层：实现能耗数据采集和存储
- UI 层：创建 `EnergyView.xaml` 展示能耗趋势图

---

## 常用代码片段

### 发布事件
```csharp
_eventBus.Publish(new NewAlarmEvent
{
    AlarmCode = "E001",
    Message = "温度超限",
    Level = AlarmLevel.Warning,
    Timestamp = DateTime.Now
});
```

### 订阅事件
```csharp
_eventBus.Subscribe<NewAlarmEvent>(OnNewAlarm);

private void OnNewAlarm(NewAlarmEvent e)
{
    // 处理报警事件
}
```

### 读取 I/O
```csharp
bool di0 = _ioDevice.ReadInput(0);        // 读数字输入 DI0
double ai0 = _ioDevice.ReadAnalog(200);   // 读模拟输入 AI0
_ioDevice.WriteOutput(100, true);          // 写数字输出 DO0
_ioDevice.WriteAnalog(300, 5.0);           // 写模拟输出 AO0
```

### EF Core 查询
```csharp
var alarms = await _dbContext.AlarmRecords
    .Where(a => a.Level == AlarmLevel.Critical)
    .OrderByDescending(a => a.Timestamp)
    .Take(50)
    .ToListAsync();
```

### Dapper 查询
```csharp
var alarms = await _connection.QueryAsync<AlarmRecord>(
    "SELECT * FROM AlarmRecords WHERE Level = @Level ORDER BY Timestamp DESC LIMIT @Count",
    new { Level = "Critical", Count = 50 }
);
```

---

## 推荐学习资源

| 主题 | 推荐资源 |
|------|----------|
| C# 基础 | Microsoft Learn — C# 文档 |
| WPF + MVVM | Microsoft WPF 文档 / Rachel Lim's Blog |
| Entity Framework Core | EF Core 官方文档 |
| 工业 MES 概念 | 项目 docs/ 目录下的设计文档 |
| 运动控制 G-code | LinuxCNC G-code 参考手册 |

---

## 项目文档索引（docs/）

| 文件 | 内容 |
|------|------|
| 01_详细设计文档.md | 系统详细设计 |
| 02_项目规程报告.md | 项目规程报告 v2 |
| 03_快速启动指南.md | 快速启动指南 |
| 04_v3规程报告.md | v3 规程报告 |
| 11_v3.1.2_执行开发文档.md | v3.1.2 执行开发文档 |

> 建议在完成阶段二后，通读 `docs/` 目录下的所有文档，会对系统设计有更深的理解。

---

*本学习指南基于 SmartMES V9.0 代码分析生成，建议结合实际代码阅读使用。*
