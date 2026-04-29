# SmartMES V9.0 快速启动指南

**版本：** V9.0  
**更新日期：** 2026-04-29  

---

## 一、环境要求

| 要求项 | 最低版本 | 推荐版本 |
|--------|---------|---------|
| 操作系统 | Windows 10 (64位) | Windows 11 Pro |
| .NET 运行时 | .NET 9.0 | .NET 9.0 最新补丁 |
| 内存 | 4 GB RAM | 8 GB RAM |
| 显示器分辨率 | 1280 × 720 | 1920 × 1080 或以上 |
| Visual Studio | 2022 17.8+ | 2022 最新版 |

---

## 二、从源码编译启动（开发者）

### 步骤 1：克隆/获取源码

```
获取路径：D:\软件\Gemi\SmartMES -V9.0\
```

### 步骤 2：还原 NuGet 包

```bash
cd "D:\软件\Gemi\SmartMES -V9.0"
dotnet restore SmartMES.sln
```

### 步骤 3：编译解决方案

```bash
dotnet build SmartMES.sln --configuration Release
```

### 步骤 4：运行测试（可选）

```bash
dotnet test SmartMES.Tests/SmartMES.Tests.csproj --configuration Debug
```

预期结果：通过 120+ 项，跳过 5 项（集成测试），失败 0 项。

### 步骤 5：启动应用

```bash
dotnet run --project SmartMES.UI/SmartMES.UI.csproj --configuration Release
```

或直接在 Visual Studio 中按 **F5** 启动。

---

## 三、配置文件快速设置

### 3.1 配置文件位置

首次运行后，程序在以下位置自动创建默认配置文件：

```
应用目录/Config/appsettings.prod.json
```

### 3.2 关键配置项说明

```json
{
  "RunMode": "Simulation",          // "Simulation"=仿真模式 / "Real"=真实硬件
  "TcpServerIp": "127.0.0.1",       // 上位机 TCP 地址
  "TcpServerPort": 9000,            // 上位机 TCP 端口
  "ModbusHostIp": "192.168.1.100",  // Modbus 设备 IP
  "ModbusPort": 502,                // Modbus TCP 端口
  "ModbusUnitId": 1,                // Modbus 从站地址
  "DatabaseType": "SQLite",         // "SQLite" / "MySQL" / "SqlServer"
  "SqliteDbPath": "SmartMES.db",    // SQLite 数据库文件路径
  "MinLogLevel": "Info"             // "Debug" / "Info" / "Warning" / "Error"
}
```

### 3.3 快速切换仿真/真实模式

**方法一：** 直接修改 `appsettings.prod.json` 中 `RunMode` 字段

**方法二：** 在应用内通过参数配置界面切换（登录管理员后可访问）

---

## 四、首次登录

启动后显示登录界面，使用默认账号登录：

| 账号角色 | 用户名 | 密码 | 可访问功能 |
|---------|--------|------|---------|
| 超级管理员 | `admin` | `admin123` | 全部功能 |
| 操作员 | `operator` | `op123` | 仪表盘、报警、日志 |
| 工程师 | `engineer` | `eng123` | 设备、通信、配方、报表 |

> ⚠️ 正式部署前请修改默认密码！

---

## 五、主界面导航说明

```
┌─────── SmartMES / [当前页面]  ───────────── 👤 用户 [角色] 时间 ──┐
│                                                                   │
│  监控                      ┌─────────────────────────────────┐   │
│  📊 实时仪表盘              │                                 │   │
│  🔌 设备管理               │         当前页面内容             │   │
│  📡 通信监控               │                                 │   │
│  🔔 报警管理               │                                 │   │
│  📋 系统日志               │                                 │   │
│  ⚡ 自动化流程              │                                 │   │
│                            │                                 │   │
│  扩展模块                  │                                 │   │
│  🏭 MES通信                │                                 │   │
│  📁 文件处理               └─────────────────────────────────┘   │
│  🗄 数据库                                                        │
│  🎯 运动控制中心                                                   │
│  ⚙ C++ Native                                                    │
│                                                                   │
│  工业级能力                                                        │
│  🏗 工业系统总览                                                   │
│  📈 报表统计                                                       │
│                                                                   │
│  系统                                                             │
│  ⚙ 参数配置                                                       │
│  👤 用户管理                                                       │
└──────────────────────────────────────────────────────────────────┘
```

---

## 六、报表统计模块使用说明

### 6.1 进入报表页面

左侧菜单 → **工业级能力** → **📈 报表统计**

### 6.2 功能说明

| 功能 | 操作 | 说明 |
|------|------|------|
| 查看报表 | 选择统计周期和日期 → 点击"🔄 刷新报表" | 刷新 KPI 卡片和表格 |
| 导出数据 | 点击"📤 导出CSV" | 产量数据导出为 CSV 文件 |
| 清空日志 | 点击"🧹 清空日志" | 清除底部操作日志 |

### 6.3 KPI 指标含义

| 指标 | 含义 | 理想范围 |
|------|------|---------|
| 总产量 | 统计周期内合格+不合格总件数 | 根据计划目标 |
| 综合良品率 | 合格品/总产量 × 100% | ≥ 95% |
| 报警总数 | 统计周期内报警触发次数 | 越低越好 |
| 运行时长 | 设备有效运行小时数 | 根据排班 |
| OEE | 设备综合效率（可用性×性能×质量） | ≥ 85% 为优秀 |

---

## 七、配方管理模块使用说明

### 7.1 配方状态流转

```
新建草稿 → [工程师审批] → 已生效 → [激活用于生产]
                                  ↓
                               [归档（保存历史）]
                                  ↓
                               已归档（只读）
```

### 7.2 操作说明

| 操作 | 前提条件 | 说明 |
|------|---------|------|
| 创建配方 | 登录工程师以上角色 | 新建为草稿状态 |
| 审批配方 | 配方处于草稿状态 | 配方变为已生效 |
| 激活配方 | 配方处于已生效状态 | 配方可用于生产 |
| 归档配方 | 配方已生效且非当前激活 | 变为历史版本 |
| 克隆版本 | 任意配方 | 基于现有配方创建新草稿 |
| 校验参数 | 任意配方 | 检查所有参数是否在允许范围内 |

---

## 八、运行单元测试

```bash
# 运行全部自动化测试（排除集成测试）
cd "D:\软件\Gemi\SmartMES -V9.0"
dotnet test SmartMES.Tests/SmartMES.Tests.csproj --configuration Debug

# 仅运行新增功能测试
dotnet test --filter "FullyQualifiedName~RecipeVersionManagementTests|FullyQualifiedName~ConfigurationCenterTests|FullyQualifiedName~ReportServiceTests"

# 运行集成测试（需要修改测试文件去掉Skip，且有可用网络环境）
dotnet test --filter "FullyQualifiedName~ModbusTcpServiceTests&FullyQualifiedName~模拟"
```

---

## 九、常见问题

### Q1：启动报"找不到 .NET 9.0 运行时"

下载安装 .NET 9.0 SDK：https://dotnet.microsoft.com/download/dotnet/9.0

### Q2：配置文件修改后不生效

配置文件在启动时加载，修改后需要**重启应用**。

### Q3：Modbus 设备连接失败

1. 检查 `appsettings.prod.json` 中 `ModbusHostIp` 和 `ModbusPort` 是否正确
2. 检查网络连通性：`ping 192.168.1.100`
3. 确认 `RunMode` 是否设为 `"Real"`（仿真模式不进行真实连接）
4. 检查防火墙是否放行端口 502

### Q4：报表数据显示为0或空

当前版本 ReportService 使用模拟数据，点击"刷新报表"按钮可重新生成随机数据。后续版本将对接真实数据库查询。

### Q5：测试运行卡住不完成

如果全套测试长时间不结束，可能遇到 TCP 端口等待超时，使用带 filter 的命令排除集成测试：
```bash
dotnet test --filter "FullyQualifiedName!~ModbusTcpServiceTests或FullyQualifiedName~配置或FullyQualifiedName~报表或FullyQualifiedName~配方"
```

---

*快速启动指南结束 | SmartMES V9.0 | 2026-04-29*
