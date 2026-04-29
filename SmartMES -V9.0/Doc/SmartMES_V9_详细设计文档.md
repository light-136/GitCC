# SmartMES V9.0 详细设计文档

**文档版本：** V2.0  
**编制日期：** 2026-04-29  
**适用版本：** SmartMES -V9.0  

---

## 1. 文档概述

### 1.1 目的

本文档描述 SmartMES V9.0 系统的完整软件架构设计、模块划分、核心类关系及关键业务流程，供开发人员、测试人员和维护人员参考。

### 1.2 范围

本文档覆盖 SmartMES V9.0 的全部核心功能模块，包括本次迭代新增/完善的：
- Modbus TCP 通信协议实现
- 分层配置中心
- 报表统计模块
- 配方版本管理系统
- 单元测试套件

---

## 2. 系统架构设计

### 2.1 总体架构

SmartMES V9.0 采用 **WPF + .NET 9.0 + MVVM** 架构，共分五层：

```
┌─────────────────────────────────────────────────────────┐
│               SmartMES.UI（表现层）                      │
│  WPF XAML / DataTemplate / MainWindow / 各模块 View     │
├─────────────────────────────────────────────────────────┤
│              SmartMES.Services（服务层）                 │
│  AlarmService / ModbusTcpService / ReportService        │
│  ConfigurationCenter / 其他业务服务                     │
├─────────────────────────────────────────────────────────┤
│              SmartMES.Modules（功能模块层）              │
│  Motion / Vision / Industrial / Report / MesComm 等     │
├─────────────────────────────────────────────────────────┤
│               SmartMES.Core（核心层）                   │
│  接口定义 / 领域模型 / 状态机 / 配方 / 安全 / 基础设施  │
├─────────────────────────────────────────────────────────┤
│         SmartMES.Native（C++ 原生层，可选）             │
│  高性能运动控制 / 硬件 I/O 直接访问                    │
└─────────────────────────────────────────────────────────┘
```

### 2.2 项目结构

| 项目 | 类型 | 职责 |
|------|------|------|
| `SmartMES.Core` | 类库 | 接口定义、领域模型、枚举、基础设施（RelayCommand、ViewModelBase） |
| `SmartMES.Services` | 类库 | 具体业务服务实现（报警、日志、通信、配置、报表） |
| `SmartMES.Modules` | 类库 | 各扩展功能模块（运动、视觉、MES通信、数据库等） |
| `SmartMES.UI` | WPF App | 用户界面层，包括主窗口、视图、ViewModel |
| `SmartMES.Tests` | xUnit | 单元测试和集成测试 |

### 2.3 依赖方向

```
SmartMES.Tests ─────────────────────────────────────────┐
SmartMES.UI ──→ SmartMES.Services ──→ SmartMES.Core     │
SmartMES.UI ──→ SmartMES.Modules  ──→ SmartMES.Core     │←─
SmartMES.Services ──→ SmartMES.Core                     │
SmartMES.Modules  ──→ SmartMES.Core                     │
```

---

## 3. 核心接口设计

### 3.1 ICommunicationService（通信服务接口）

```csharp
// 位于 SmartMES.Core.Interfaces
public interface ICommunicationService
{
    bool IsConnected { get; }
    string ProtocolName { get; }
    event EventHandler<byte[]>? DataReceived;
    event EventHandler<bool>? ConnectionChanged;

    Task ConnectAsync();
    Task DisconnectAsync();
    Task SendAsync(byte[] data);
    Task<byte[]> ReceiveAsync();
}
```

**设计意图：** 协议无关抽象层，支持 Modbus TCP、串口、OPC UA 等多种协议的统一切换。

### 3.2 ILoggingService（日志服务接口）

```csharp
public interface ILoggingService
{
    void LogInfo(string message, string source = "System");
    void LogWarning(string message, string source = "System");
    void LogError(string message, string source = "System");
    void LogCommunication(string message, string source = "Communication");
    IReadOnlyList<LogEntry> GetLogs();
    event EventHandler<LogEntry>? LogAdded;
}
```

### 3.3 IRecipeService（配方服务接口）

```csharp
public interface IRecipeService
{
    IReadOnlyList<RecipeModel> GetAll();
    RecipeModel? GetByName(string name);
    RecipeModel? ActiveRecipe { get; }

    // 基础 CRUD
    bool Activate(string name);
    void Add(RecipeModel recipe);
    void Remove(string name);

    // 版本管理（V9.0 新增）
    bool Approve(string name, string approvedBy);
    bool Archive(string name);
    RecipeModel? CloneAsNewVersion(string name, string desc = "");
    List<string> ValidateRecipe(string name);

    // 持久化
    Task SaveAsync(string filePath);
    Task LoadAsync(string filePath);

    // 事件
    event EventHandler<RecipeModel>? RecipeActivated;
    event EventHandler<RecipeModel>? RecipeStatusChanged;
}
```

---

## 4. 核心模块详细设计

### 4.1 Modbus TCP 通信服务（ModbusTcpService）

**文件：** `SmartMES.Services/Communication/ModbusTcpService.cs`

#### 4.1.1 设计目标

实现标准 Modbus TCP 协议，支持工业现场长连接、心跳检测和自动重连。

#### 4.1.2 MBAP 报文格式

```
┌──────────┬──────────┬──────────┬──────────┬──────────┬──────────┐
│ 事务ID(2)│协议ID(2) │ 长度(2)  │从站ID(1) │功能码(1) │数据(N)   │
└──────────┴──────────┴──────────┴──────────┴──────────┴──────────┘
   MBAP 头（6字节）               PDU（功能码+数据）
```

#### 4.1.3 支持的功能码

| 功能码 | 十六进制 | 操作 |
|--------|---------|------|
| 01 | 0x01 | 读线圈（Read Coils） |
| 03 | 0x03 | 读保持寄存器（Read Holding Registers） |
| 05 | 0x05 | 写单个线圈（Write Single Coil） |
| 06 | 0x06 | 写单个寄存器（Write Single Register） |
| 16 | 0x10 | 写多个寄存器（Write Multiple Registers） |

#### 4.1.4 心跳机制

```
连接建立
    │
    ├── 启动心跳 Timer（10秒间隔）
    │
    └── HeartbeatCallback()
         ├── 读寄存器 0 作为探测
         │   ├── 成功 → 继续
         │   └── 失败 → 触发重连
         │        ├── _isReconnecting = true（防重入）
         │        ├── 关闭旧连接
         │        ├── ConnectAsync()
         │        └── _isReconnecting = false
         └── 循环
```

#### 4.1.5 线程安全

- `_sendLock`：保证发送+接收序列不被并发打断
- `_isReconnecting`：简单布尔标志防止心跳重连重入

### 4.2 分层配置中心（ConfigurationCenter）

**文件：** `SmartMES.Services/ConfigurationCenter.cs`

#### 4.2.1 设计目标

在原有 SettingsService 基础上扩展，支持分环境、可验证、可导入导出的配置管理，与现有代码并存不冲突。

#### 4.2.2 AppConfiguration 配置域划分

| 域 | 关键属性 | 用途 |
|----|---------|------|
| 系统基础 | SystemName, RunMode, Version | 运行模式切换（仿真/真实） |
| TCP 通信 | TcpServerIp, TcpServerPort | 上位机通信 |
| Modbus | ModbusHostIp, ModbusPort, ModbusUnitId | 设备直连 |
| 串口 | SerialPortName, SerialBaudRate | 串行通信 |
| 数据库 | DatabaseType, SqliteDbPath | 数据存储 |
| 日志 | LogDirectory, LogRetentionDays, MaxLogEntries | 日志管理 |
| MES | MesProtocol, MesHttpBaseUrl, MqttBrokerHost | 上层系统对接 |
| 报警阈值 | TemperatureAlarmThreshold, PressureAlarmThreshold | 工艺参数监控 |
| 界面 | UiRefreshIntervalMs, ChartHistoryPoints | UI 性能调优 |

#### 4.2.3 配置文件路径规则

```
Config/
├── appsettings.prod.json    ← 生产环境（默认）
├── appsettings.dev.json     ← 开发调试
└── appsettings.test.json    ← 测试环境
```

#### 4.2.4 配置验证规则

| 配置项 | 有效范围 |
|--------|---------|
| TcpServerPort | 1 ~ 65535 |
| ModbusPort | 1 ~ 65535 |
| DataSamplingIntervalMs | 10 ~ 60000 ms |
| LogRetentionDays | 1 ~ 365 天 |
| MaxLogEntries | 100 ~ 100000 条 |
| TemperatureAlarmThreshold | 0 ~ 500 ℃ |

### 4.3 报表统计模块（ReportModule）

**目录：** `SmartMES.UI/Modules/ReportModule/`  
**服务：** `SmartMES.Services/Report/ReportService.cs`

#### 4.3.1 模块层次

```
ReportView.xaml（视图层）
    └── DataTemplate → ReportViewModel（ViewModel 层）
         └── ReportService（服务层）
```

#### 4.3.2 界面布局

```
┌────────────────── 工具栏（周期选择/日期/按钮）──────────────────┐
├──── KPI卡片：总产量 | 良品率 | 报警数 | 运行时长 | OEE | 更新时间 ──┤
├──────────────────── 主内容区 ──────────────────────────────────┤
│  左：产量报表（按小时DataGrid）  │  右：报警统计 Top10 DataGrid  │
├────────────────── 底部操作日志 ──────────────────────────────┘
```

#### 4.3.3 数据模型

**ProductionReportRow（产量报表行）**
- 计算属性 `PassRate = PassQty/ActualQty * 100`
- 计算属性 `AchieveRate = ActualQty/PlannedQty * 100`

**SystemSummary（系统汇总）**
- `OeePercent`：设备综合效率（可用性 × 性能 × 质量）
- `OverallPassRate`：综合良品率（保留2位小数）

### 4.4 配方版本管理系统（RecipeService V2）

**文件：** `SmartMES.Core/Recipe/RecipeService.cs`

#### 4.4.1 状态机流转

```
                  Approve(name, user)
Draft ────────────────────────────→ Active
  ↑                                   │
  │  CloneAsNewVersion(name)          │ Archive(name)
  └──────────────────────────────     ↓
                                   Archived（只读，不可激活）

注意：
 - 只有 Active 状态的配方才能被 Activate()（用于生产）
 - 已激活配方不可 Archive（必须先切换到其他配方）
 - 已激活配方不可 Remove
```

#### 4.4.2 克隆版本命名规则

```
原配方名：产品A V1.2
克隆版本：产品A V2.0（主版本号+1，次版本号归零）
若重名：  产品A V2.0_04291530（追加时间戳）
```

#### 4.4.3 变更日志

- 每次 Approve、Archive、Clone 操作自动追加变更日志
- 最多保留最近 **50 条**（FIFO 淘汰旧记录）
- 每条记录包含：时间、操作人、描述、版本前后对比

---

## 5. 依赖注入与对象生命周期

SmartMES V9.0 不使用 DI 框架，采用手动构建（在 `App.xaml.cs` 中集中创建）：

```csharp
// App.xaml.cs 启动顺序（简化示意）
var logger = new LoggingService();
var eventBus = new EventBus();
var settings = new SettingsService();
var alarmService = new AlarmService(eventBus, logger);
var recipeService = new RecipeService();

// 各 ViewModel 按需注入
var dashboardVM = new DashboardViewModel(logger, alarmService, recipeService, ...);
var mainVM = new MainViewModel(eventBus, logger, dashboardVM, ...);
```

所有 ViewModel 均为**单例**（在 App.xaml.cs 创建后传入 MainViewModel，整个生命周期保持存活）。

---

## 6. 线程安全设计

| 组件 | 线程安全机制 |
|------|-------------|
| RecipeService | `lock(_lock)` 保护 `_recipes` 集合所有读写 |
| ModbusTcpService | `lock(_sendLock)` 保护 TCP 发送+接收序列；`_isReconnecting` 防止重连重入 |
| AlarmService | `lock(_lock)` 保护报警列表 |
| AxisController | `lock(_stateLock)` 保护状态转换；`lock(_posLock)` 保护位置数据（分离锁避免死锁） |
| StateMachineEngine | `lock(_lock)` 保护状态字典 |

---

## 7. 事件总线（EventBus）设计

EventBus 基于 `ConcurrentDictionary<Type, List<Delegate>>` 实现发布/订阅：

```
UserLoginEvent
    └── 发布：UserViewModel.Login()
    └── 订阅：MainViewModel → 更新权限、刷新菜单可见性

AlarmTriggeredEvent
    └── 发布：AlarmService.TriggerAlarm()
    └── 订阅：DashboardViewModel → 更新报警计数

RecipeActivatedEvent
    └── 发布：RecipeService.Activate()
    └── 订阅：DashboardViewModel → 更新当前配方显示
```

---

## 8. 页面权限控制

基于 `PagePermissions` 对象，每个菜单按钮通过绑定 `CanAccessXxx` 属性（转换器控制 Visibility）实现访问控制。

权限检查逻辑：
```csharp
private bool HasPermission(Func<PagePermissions, bool> selector)
    => _currentPermissions == null || selector(_currentPermissions);
// 未登录时（_currentPermissions == null）所有权限开放
```

---

## 9. 数据持久化

### 9.1 配方持久化

- 格式：UTF-8 JSON（`JsonSerializer` + `UnsafeRelaxedJsonEscaping`）
- 接口：`SaveAsync(filePath)` / `LoadAsync(filePath)`
- 存储：用户自定义路径，建议 `Data/recipes.json`

### 9.2 配置持久化

- 格式：UTF-8 JSON，按环境分文件
- 路径：`Config/appsettings.{env}.json`
- 首次运行自动创建默认配置文件

### 9.3 日志持久化

- 内存环形缓冲（最多 `MaxLogEntries` 条）
- 可通过 `LogDirectory` 配置落盘目录

---

## 10. 编码规范

1. 所有注释、变量描述、提示信息使用**简体中文**
2. 类和公开方法必须有 XML Doc 注释（`<summary>` 标签）
3. 锁命名规则：`_xxxLock`（私有只读字段）
4. 异步方法后缀 `Async`，Task 返回类型
5. 事件参数名：发送方 `sender`，事件数据 `e` 或具体类型
6. 文件编码：**UTF-8 with BOM**（避免 GBK 乱码）

---

*文档结束 | SmartMES V9.0 | 2026-04-29*
