<!-- markdownlint-disable -->
# SmartMES V9.0 项目总结文档

> 生成日期：2026-04-27

---

## 一、项目概述

| 项目 | 内容 |
|------|------|
| 项目名称 | SmartMES 智能制造上位机系统 |
| 版本 | V9.0（内部文档标注 v3.0.0） |
| 平台 | .NET 9.0 + WPF（Windows 桌面应用） |
| 定位 | 工业级 MES（制造执行系统）上位机软件 |
| 语言 | C#（主体）+ C++（原生性能模块） |

---

## 二、技术栈

### 核心框架
- **.NET 9.0** — 运行时与编译目标
- **WPF（Windows Presentation Foundation）** — 桌面 UI 框架，使用 XAML 描述界面
- **C++（SmartMES.Native）** — 高性能原生模块，通过 P/Invoke 与 C# 互操作

### 数据层
| 组件 | 版本 | 用途 |
|------|------|------|
| Entity Framework Core | 9.0.5 | ORM，支持 SQLite / SQL Server / MySQL |
| Dapper | 2.1.66 | 轻量级 SQL 查询 |
| Pomelo.EFCore.MySql | 9.0.0 | MySQL 驱动 |
| Microsoft.Data.Sqlite | 9.0.5 | SQLite ADO.NET 驱动 |

### 其他依赖
- **EPPlus 7.7.0** — Excel 文件读写
- **System.Text.Json 9.0.5** — JSON 序列化
- **xUnit 2.9.3 + coverlet** — 单元测试与覆盖率

---

## 三、项目结构

```
SmartMES/
├── SmartMES.Core/          # 核心层：接口、模型、基础设施
├── SmartMES.Services/      # 服务层：业务逻辑实现
├── SmartMES.Modules/       # 模块层：各功能业务模块
├── SmartMES.UI/            # 表现层：WPF 界面
├── SmartMES.Tests/         # 测试层：xUnit 单元测试
├── SmartMES.Native/        # 原生层：C++ 高性能模块
├── docs/                   # 项目文档（20+ 篇 Markdown）
└── Logs/                   # 运行时日志目录
```

### 依赖关系（单向）
```
UI  →  Services  →  Core
UI  →  Modules   →  Core
Tests → 所有层
```

---

## 四、核心模块说明

### 4.1 SmartMES.Core（核心层）

| 子模块 | 职责 |
|--------|------|
| Scheduler | 任务调度与编排 |
| StateMachine | 工业控制状态机引擎 |
| IO | 数字/模拟量 I/O 控制（含仿真设备） |
| Recipe | 制造工艺配方管理 |
| Safety | 安全联锁系统 |
| Traceability | 产品/工艺追溯 |
| Plugin | 插件扩展架构 |
| EventBus | 事件总线（发布/订阅解耦） |

### 4.2 SmartMES.Modules（业务模块层）

| 模块 | 职责 |
|------|------|
| MotionControl | 3 轴运动控制 |
| Motion10Axis | 10 轴运动控制（支持 G-code） |
| Vision | 视觉检测与图像处理 |
| VisionMotion | 视觉+运动协同调度 |
| Database | 多数据库操作（EF Core + Dapper） |
| FileProcess | 文件上传/下载/处理 |

### 4.3 SmartMES.UI（界面层）

| 视图 | 功能 |
|------|------|
| DashboardView | 实时监控仪表盘 |
| DeviceView | 设备管理与状态 |
| CommunicationView | 通信协议监控（TCP/串口/OPC-UA） |
| AlarmView | 报警历史与管理 |
| LogView | 系统事件日志 |
| AutomationView | 工作流自动化 |
| SettingsView | 系统参数配置 |
| UserView | 用户与角色权限管理（16 页权限） |

---

## 五、I/O 系统

| 类型 | 通道数 | 地址范围 |
|------|--------|----------|
| 数字输入（DI） | 16 | 0 – 15 |
| 数字输出（DO） | 16 | 100 – 115 |
| 模拟输入（AI） | 4 | 200 – 203 |
| 模拟输出（AO） | 4 | 300 – 303 |

> 内置 `SimulatedIoDevice`，无需真实硬件即可运行和测试。

---

## 六、数据模型

| 模型 | 说明 |
|------|------|
| AlarmRecord | 报警记录（代码、消息、级别、时间戳） |
| DeviceModel | 设备配置与状态 |
| LogEntry | 系统事件日志条目 |
| SystemSettings | 系统配置参数 |
| UserModel | 用户账号与角色 |

---

## 七、系统配置（settings.json）

| 参数 | 默认值 | 说明 |
|------|--------|------|
| DataSamplingIntervalMs | 1000 | 数据采集间隔（毫秒） |
| TemperatureAlarmThreshold | 85°C | 温度报警阈值 |
| PressureAlarmThreshold | 10 MPa | 压力报警阈值 |
| SpeedAlarmThreshold | 3000 rpm | 转速报警阈值 |
| TcpServerIp | 127.0.0.1 | TCP 服务器地址 |
| TcpServerPort | 9000 | TCP 服务器端口 |
| SerialPortName | COM1 | 串口名称 |
| SerialBaudRate | 9600 | 串口波特率 |
| LogRetentionDays | 30 | 日志保留天数 |
| MaxLogEntries | 1000 | 最大日志条数 |

---

## 八、内置账号

| 账号 | 密码 | 角色 |
|------|------|------|
| admin | admin123 | 管理员（全权限） |
| operator | op123 | 操作员 |
| viewer | view123 | 只读访问 |

---

## 九、快速启动

```bash
cd "D:/软件/Cursor/文档存放/代码区/SmartMES -V9.0 - 副本"
dotnet build SmartMES.sln
dotnet run --project SmartMES.UI
```

---

## 十、项目亮点

1. **分层架构清晰**：Core → Services → Modules → UI，单向依赖，职责分明
2. **多数据库支持**：SQLite（开发）/ SQL Server / MySQL（生产）无缝切换
3. **硬件仿真**：SimulatedIoDevice 让开发者无需硬件即可完整运行系统
4. **事件总线解耦**：各模块通过 EventBus 通信，避免直接依赖
5. **工业级功能完整**：调度、状态机、配方、安全联锁、追溯、插件一应俱全
6. **完善文档**：docs/ 目录包含 20+ 篇设计文档、规程报告、学习指南
