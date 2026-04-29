# SmartHMI 工业上位机系统 — 详细设计文档

**版本：** 1.0  
**日期：** 2026-04-27  
**技术栈：** .NET 9.0 + WPF + MVVM + EF Core 9 + SQLite

---

## 1. 系统架构总览

### 1.1 分层架构

```
┌─────────────────────────────────────────────────────┐
│                  SmartHMI.UI                        │
│  Views (XAML) ←→ ViewModels ←→ INavigationService  │
├─────────────────────────────────────────────────────┤
│                SmartHMI.Modules                     │
│  DeviceManager │ CommunicationManager │ MotionMgr   │
├─────────────────────────────────────────────────────┤
│                SmartHMI.Services                    │
│  AlarmService │ UserService │ LoggingService │ ...  │
├─────────────────────────────────────────────────────┤
│                 SmartHMI.Core                       │
│  Models │ Interfaces │ EventBus │ StateMachine │ IO │
└─────────────────────────────────────────────────────┘
```

### 1.2 项目依赖关系

```
SmartHMI.UI  →  SmartHMI.Modules  →  SmartHMI.Services  →  SmartHMI.Core
SmartHMI.Tests  →  SmartHMI.Core / Services / Modules
```

依赖方向单向向下，上层不被下层引用，符合依赖倒置原则。

---

## 2. SmartHMI.Core — 核心层

### 2.1 领域模型

| 模型类 | 关键属性 | 说明 |
|--------|----------|------|
| `DeviceModel` | Id, Name, Type, Status, IpAddress, Port | 设备信息 |
| `AlarmRecord` | Id, Code, Message, Level, TriggeredAt, ClearedAt | 报警记录 |
| `LogEntry` | Id, Timestamp, Level, Module, Message | 日志条目 |
| `UserModel` | Username, PasswordHash, Role, IsActive | 用户信息 |
| `CommunicationChannel` | Id, Name, Protocol, Endpoint, Status | 通信通道 |
| `AxisModel` | Id, Name, State, Position, Velocity | 运动轴 |
| `SystemSettings` | RefreshInterval, MaxAlarms, DbPath 等 | 系统配置 |

### 2.2 枚举定义

```csharp
enum DeviceStatus   { Offline, Connecting, Online, Faulted, Maintenance }
enum DeviceType     { PLC, Sensor, IoModule, Axis, Camera, Controller, Instrument, Other }
enum AlarmLevel     { Info, Warning, Error, Critical }
enum UserRole       { Viewer, Operator, Engineer, Admin }
enum ConnectionStatus { Disconnected, Connecting, Connected, Reconnecting, Error }
enum ProtocolType   { TCP, Serial, MQTT, OpcUa }
enum AxisState      { Disabled, Enabled, Homing, Idle, Moving, Jogging, Faulted, EStop }
enum IoChannelType  { DI, DO, AI, AO }
enum DeviceState    { Idle, Initializing, Ready, Running, Paused, Stopping, Faulted, Maintenance, EStop }
```

### 2.3 EventAggregator（事件总线）

**实现：** `SmartHMI.Core.EventBus.EventAggregator`  
**接口：** `IEventBus`

```csharp
public interface IEventBus {
    void Subscribe<TEvent>(Action<TEvent> handler);
    void Unsubscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent evt);
}
```

内部使用 `Dictionary<Type, List<Delegate>>` 存储订阅者，`Lock` 保证线程安全，发布时先复制快照再遍历，避免在回调中修改集合导致的死锁。

**应用事件列表：**

| 事件类 | 触发时机 |
|--------|----------|
| `DeviceStatusChangedEvent` | 设备状态变化 |
| `NewAlarmEvent` | 新报警触发 |
| `AlarmClearedEvent` | 报警清除 |
| `UserLoginEvent` | 用户登录/登出 |
| `CommunicationStatusChangedEvent` | 通信通道状态变化 |
| `AxisStateChangedEvent` | 运动轴状态变化 |
| `IoChannelChangedEvent` | IO 通道值变化 |

### 2.4 DeviceStateMachine（状态机）

标准工业设备状态转换图：

```
Idle ──Initialize──► Initializing ──InitComplete──► Ready ──Start──► Running
                          │                           │                  │
                       InitFailed                EnterMaint           Pause
                          │                           │                  │
                          ▼                           ▼                  ▼
                       Faulted ◄──────────────── Maintenance         Paused
                          │                                             │
                        Reset                                        Resume
                          │                                             │
                          └──────────────────────────────────────────► ▲
                                                                        │
Running ──Stop──► Stopping ──StopComplete──► Idle
任意状态 ──EStop──► EStop ──EStopReset──► Idle
```

### 2.5 SimulatedIoDevice（IO 仿真）

地址映射：

| 类型 | 地址范围 | 数量 | 说明 |
|------|----------|------|------|
| DI | 0–15 | 16 | 数字输入，随机翻转 |
| DO | 100–115 | 16 | 数字输出，可写 |
| AI | 200–203 | 4 | 模拟输入，正弦波仿真 |
| AO | 300–303 | 4 | 模拟输出，可写 |

---

## 3. SmartHMI.Services — 服务层

### 3.1 AlarmService

- 维护 `_active`（活动报警）和 `_history`（历史，最多 500 条）两个列表
- `Trigger()` → 创建 AlarmRecord → 加入两个列表 → 发布 `NewAlarmEvent`
- `Acknowledge()` → 设置 `AcknowledgedAt`
- `Clear()` → 设置 `ClearedAt` → 从 `_active` 移除 → 发布 `AlarmClearedEvent`
- `ClearAll()` → 批量清除所有活动报警

### 3.2 UserService

- 内置 4 个默认用户（admin/engineer/operator/viewer）
- 密码使用 SHA-256 哈希存储
- `Login()` → 验证哈希 → 设置 `CurrentUser` → 发布 `UserLoginEvent{IsLogin=true}`
- `Logout()` → 清空 `CurrentUser` → 发布 `UserLoginEvent{IsLogin=false}`

### 3.3 LoggingService

- 内存环形缓冲，最多 2000 条
- 提供 `Log(level, module, message)` 方法
- `EntryAdded` 事件供 UI 订阅实时刷新

### 3.4 SettingsService

- 配置持久化到 `appsettings.json`（JSON 序列化）
- `Load()` / `Save()` 方法
- 首次运行自动创建默认配置

---

## 4. SmartHMI.Modules — 业务模块层

### 4.1 DeviceManager

- 初始化 8 台仿真设备（PLC、传感器、IO 模块等）
- 每 3 秒执行健康检查，随机模拟设备状态变化
- 状态变化时发布 `DeviceStatusChangedEvent`

### 4.2 CommunicationManager

- 管理 4 条通信通道（TCP、Serial、MQTT、OPC UA）
- TCP 通道使用真实 `TcpCommunicationClient`（异步收发）
- 其他通道使用仿真模式（模拟连接延迟和数据收发）
- 状态变化时发布 `CommunicationStatusChangedEvent`

### 4.3 MotionManager / AxisController

- 管理 X/Y/Z 三轴
- 每轴支持：Enable/Disable、Home、MoveToPosition、Jog、EStop、Reset
- 200ms 仿真定时器更新轴位置
- 状态变化时发布 `AxisStateChangedEvent`

### 4.4 AppDbContext（EF Core）

```csharp
DbSet<AlarmRecord> AlarmRecords
DbSet<LogEntry>    LogEntries
DbSet<DeviceModel> Devices
```

SQLite 数据库，路径由 `SystemSettings.DatabasePath` 配置。

---

## 5. SmartHMI.UI — 表现层

### 5.1 MVVM 架构

```
App.xaml (DataTemplate ViewLocator)
    ↓ 自动映射
ContentControl.Content = CurrentViewModel
    ↓ WPF 自动渲染
对应 View (UserControl)
```

**DataTemplate 映射表：**

| ViewModel | View |
|-----------|------|
| `LoginViewModel` | `LoginView` |
| `DashboardViewModel` | `DashboardView` |
| `DeviceViewModel` | `DeviceView` |
| `AlarmViewModel` | `AlarmView` |
| `LogViewModel` | `LogView` |
| `CommunicationViewModel` | `CommunicationView` |
| `MotionViewModel` | `MotionView` |
| `SettingsViewModel` | `SettingsView` |
| `UserViewModel` | `UserView` |

### 5.2 INavigationService

```csharp
public interface INavigationService {
    void NavigateTo<TViewModel>() where TViewModel : class;
    object? CurrentViewModel { get; }
    event EventHandler? CurrentViewModelChanged;
}
```

`NavigationService` 实现通过 `IServiceProvider` 解析 ViewModel 实例，支持 Singleton 和 Transient 两种生命周期。

### 5.3 MainViewModel 导航逻辑

```
用户点击侧边栏按钮
    → NavigateCommand.Execute("Dashboard")
    → _navigationService.NavigateTo<DashboardViewModel>()
    → CurrentViewModelChanged 事件
    → MainViewModel.CurrentViewModel 更新
    → ContentControl 自动渲染 DashboardView
```

登录/登出自动导航：
- `UserLoginEvent{IsLogin=true}` → 导航到 Dashboard
- `UserLoginEvent{IsLogin=false}` → 导航到 Login

### 5.4 PasswordBoxHelper（附加属性）

解决 WPF `PasswordBox` 不支持 MVVM 绑定的问题：

```xml
<PasswordBox helpers:PasswordBoxHelper.BindPassword="True"
             helpers:PasswordBoxHelper.BoundPassword="{Binding Password, Mode=TwoWay}"/>
```

原理：监听 `PasswordChanged` 事件，将密码同步到附加属性，再通过绑定传递给 ViewModel。

### 5.5 全局值转换器

在 `Styles.xaml` 中注册，所有 View 均可直接使用：

| Key | 类型 | 转换逻辑 |
|-----|------|----------|
| `BoolToVisible` | `BoolToVisibilityConverter` | true→Visible, false→Collapsed |
| `PosToVis` | `PositiveToVisibilityConverter` | >0→Visible, else→Collapsed |
| `BoolToOnOff` | `BoolToOnOffConverter` | true→"ON", false→"OFF" |
| `StatusColor` | `StatusToColorConverter` | 状态字符串→颜色 |

### 5.6 UI 主题色板

| 资源 Key | 颜色值 | 用途 |
|----------|--------|------|
| `BgDark` | #1A1A2E | 主背景 |
| `BgPanel` | #252540 | 面板背景 |
| `BgSidebar` | #16213E | 侧边栏背景 |
| `BgCard` | #2D2D4E | 卡片背景 |
| `AccentCyan` | #00D4FF | 主强调色 |
| `AccentGreen` | #00FF88 | 在线/成功 |
| `AccentAmber` | #FFB800 | 警告 |
| `AccentRed` | #FF4444 | 错误/报警 |

---

## 6. 依赖注入配置

在 `App.xaml.cs` 中通过 `Microsoft.Extensions.DependencyInjection` 注册：

```csharp
// Core
services.AddSingleton<IEventBus, EventAggregator>();

// Services
services.AddSingleton<IAlarmService, AlarmService>();
services.AddSingleton<IUserService, UserService>();
services.AddSingleton<ILoggingService, LoggingService>();
services.AddSingleton<ISettingsService, SettingsService>();

// Modules
services.AddSingleton<DeviceManager>();
services.AddSingleton<CommunicationManager>();
services.AddSingleton<MotionManager>();

// UI
services.AddSingleton<INavigationService, NavigationService>();
services.AddSingleton<MainViewModel>();
services.AddSingleton<MainWindow>();
services.AddTransient<LoginViewModel>();
services.AddSingleton<DashboardViewModel>();
// ... 其余 ViewModel
```

---

## 7. 数据流示意

### 7.1 登录流程

```
用户输入账号密码 → LoginViewModel.LoginCommand
    → IUserService.Login(username, password)
    → 验证 SHA-256 哈希
    → 发布 UserLoginEvent{IsLogin=true}
    → MainViewModel 订阅事件 → NavigateTo<DashboardViewModel>
    → ContentControl 渲染 DashboardView
```

### 7.2 报警触发流程

```
DeviceManager 健康检查 → 设备状态异常
    → IAlarmService.Trigger("A001", "设备离线", Error)
    → 发布 NewAlarmEvent
    → AlarmViewModel 订阅 → 更新 ActiveAlarms 列表
    → DashboardViewModel 订阅 → 更新 ActiveAlarmCount
    → MainViewModel 订阅 → 更新顶部报警徽章
```

---

## 8. 关键设计决策

| 决策 | 方案 | 原因 |
|------|------|------|
| ViewModel→View 映射 | DataTemplate ViewLocator | 零 code-behind，纯 MVVM |
| 导航 | INavigationService + IServiceProvider | ViewModel 不依赖 View |
| 密码绑定 | PasswordBoxHelper 附加属性 | PasswordBox 不支持直接绑定 |
| 模块解耦 | EventAggregator 事件总线 | 模块间无直接引用 |
| 硬件仿真 | SimulatedIoDevice + Timer | 无需真实硬件即可运行 |
| 数据库 | EF Core + SQLite | 轻量，无需安装数据库服务 |
