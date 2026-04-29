# SmartHMI 第3-6阶段完整设计文档

**版本：** 2.0  
**日期：** 2026-04-27  
**覆盖阶段：** Phase 3（安全/视觉）、Phase 4（多轴/视觉协同）、Phase 5（配方/追溯/报表/MES）、Phase 6（云端/SECS-GEM/AI）

---

## 一、第三阶段：安全互锁 & 程序执行

### 1.1 SafetyInterlockService

**文件：** `SmartHMI.Modules/Safety/SafetyInterlockService.cs`  
**接口：** `ISafetyInterlockService`

核心设计：条件注册表 + 急停状态机

```
RegisterCondition(name, Func<bool>, description)
    ↓
CheckAll() → 遍历所有条件，全部返回 true 则安全
    ↓
TriggerEStop(reason) → 设置 _eStopActive = true → 触发 EStopTriggered 事件
    ↓
ResetEStop() → 仅当 CheckAll() == true 时才允许复位
```

**安全互锁条件示例：**

| 条件名 | 检查逻辑 | 说明 |
|--------|----------|------|
| 门禁传感器 | DI[0] == true | 安全门已关闭 |
| 光幕传感器 | DI[1] == true | 光幕无遮挡 |
| 气压检测 | AI[0] >= 5.0 | 气压 ≥ 5bar |
| 温度检测 | AI[1] < 85.0 | 温度 < 85°C |

### 1.2 ProgramExecutor

**文件：** `SmartHMI.Modules/Program/ProgramExecutor.cs`

步骤类型：

| StepType | 执行逻辑 |
|----------|----------|
| Action | 执行 `Func<Task<bool>>` 异步动作 |
| Condition | 检查 `Func<bool>` 条件 |
| Delay | `Task.Delay(step.Delay)` |
| Loop | 循环执行子步骤 |

状态机：`Idle → Running → Completed/Faulted`，支持 Pause/Resume/Abort/Reset。

---

## 二、第四阶段：视觉系统

### 2.1 VisionService

**文件：** `SmartHMI.Modules/Vision/VisionService.cs`  
**接口：** `IVisionService`

仿真检测逻辑：
- 95% 概率 OK，4% NG，1% Uncertain
- 模拟相机采集延迟 80-200ms
- 返回偏移量（OffsetX/Y/Angle）和测量值（Width/Height/Roundness）

### 2.2 VisionMotionCoordinator

**文件：** `SmartHMI.Modules/Vision/VisionMotionCoordinator.cs`

视觉引导定位流程：

```
AlignAsync(jobName)
    → VisionService.TriggerAsync()
    → 检查 ResultType（NG/Uncertain 返回失败）
    → MotionManager.Axes["X"].MoveToPosition(currentPos + OffsetX)
    → MotionManager.Axes["Y"].MoveToPosition(currentPos + OffsetY)
    → 返回 (Success=true, Message="偏移补偿完成")
```

### 2.3 VisionAnalysisService

**文件：** `SmartHMI.Modules/Ai/VisionAnalysisService.cs`

- 订阅 `IVisionService.ResultAvailable` 事件，维护 1000 条分析缓冲
- `GetStats(n)` 返回最近 n 条的 OK/NG/良率/缺陷分布统计
- `PredictYield(n)` 基于近期趋势预测下批次良率

---

## 三、第五阶段：配方 / 追溯 / 报表 / MES

### 3.1 RecipeService

**文件：** `SmartHMI.Modules/Recipe/RecipeService.cs`  
**接口：** `IRecipeService`

- 内存存储，支持 CRUD + SetActive
- `SetActive(id)` 将同产品类型的其他配方设为非激活，触发 `RecipeApplied` 事件
- 内置 2 个默认配方（ProductA/ProductB）

### 3.2 TraceabilityService

**文件：** `SmartHMI.Modules/Traceability/TraceabilityService.cs`  
**接口：** `ITraceabilityService`

- 内存存储，最多 10000 条
- 支持按序列号、工单号查询
- `TraceEventType`：Start / StepComplete / Inspection / Alarm / Complete / Reject

### 3.3 ReportService

**文件：** `SmartHMI.Modules/Report/ReportService.cs`  
**接口：** `IReportService`  
**依赖：** EPPlus 7.7.0

三种报表：
- 报警报表：按时间范围导出 AlarmHistory
- 生产报表：按时间范围导出 TraceRecord
- 追溯报表：按工单号导出完整追溯链

输出格式：Excel (.xlsx)，带深色主题表头（#16213E 背景 + #00D4FF 字体）。

### 3.4 MesConnector

**文件：** `SmartHMI.Modules/Mes/MesConnector.cs`  
**接口：** `IMesConnector`

仿真模式（可替换为真实 REST API）：
- `ConnectAsync()` → 生成仿真工单，触发 `WorkorderReceived` 事件
- `ReportProductionAsync(workorderId, qty, ngQty)` → 更新工单完成数
- `ReportAlarmAsync(code, message)` → 上报报警到 MES

---

## 四、第六阶段：云端同步 / SECS-GEM / AI

### 4.1 CloudSyncService

**文件：** `SmartHMI.Modules/Cloud/CloudSyncService.cs`  
**接口：** `ICloudSyncService`

架构：队列 + 批量刷新

```
EnqueueAsync(dataType, payload)
    → 序列化为 JSON → 加入 Queue<CloudSyncRecord>
    ↓
FlushAsync() (每30秒自动 / 手动触发)
    → 批量取出最多50条
    → HTTP POST 到 CloudEndpoint（仿真模式直接成功）
    → 失败时重试，最多3次
    → 触发 SyncCompleted 事件
```

### 4.2 SecsGemService

**文件：** `SmartHMI.Modules/SecsGem/SecsGemService.cs`  
**接口：** `ISecsGemService`

实现的 SECS/GEM 消息集：

| 消息 | 方向 | 说明 |
|------|------|------|
| S1F1 | E→H | Are You There（心跳请求） |
| S1F2 | H→E | I Am Online（心跳响应） |
| S1F13 | E→H | 上线请求 |
| S1F14 | H→E | 上线确认 |
| S6F11 | E→H | 事件上报（PROCESS_COMPLETE 等） |
| S6F12 | H→E | 事件确认 |

状态机：`Disabled → Enabled → Selected → OnlineRemote`

### 4.3 AiAssistantService

**文件：** `SmartHMI.Modules/Ai/AiAssistantService.cs`  
**接口：** `IAiAssistantService`

双模式：
1. **本地规则引擎**（默认）：基于报警数量、级别、设备状态生成建议
2. **LLM API 模式**（可选）：配置 `ApiEndpoint` 后调用 OpenAI 兼容接口（DeepSeek/Ollama）

分析方法：
- `AnalyzeAlarmsAsync()` → 检查活动报警数量和级别
- `AnalyzeDeviceHealthAsync()` → 设备健康度评估
- `AnalyzeProductionTrendAsync()` → 生产效率优化建议
- `ChatAsync(message)` → 对话式问答

---

## 五、UI 导航结构（完整）

```
登录页
└── 主界面
    ├── 监  控
    │   ├── 📊 仪表盘
    │   ├── 🖥 设备管理
    │   └── 📡 通信监控
    ├── 控  制
    │   ├── ⚙ 运动控制
    │   ├── 🛡 安全互锁      ← Phase 3 新增
    │   └── 👁 视觉检测      ← Phase 4 新增
    ├── 生  产
    │   ├── 📦 配方管理      ← Phase 5 新增
    │   ├── 🔍 追溯系统      ← Phase 5 新增
    │   ├── 📊 报表导出      ← Phase 5 新增
    │   └── 🏭 MES 协同      ← Phase 5 新增
    ├── 诊  断
    │   ├── 🔔 报警管理
    │   └── 📋 系统日志
    ├── 云端 / AI
    │   ├── ☁ 云端同步      ← Phase 6 新增
    │   ├── ⚡ SECS/GEM     ← Phase 6 新增
    │   └── 🤖 AI 助手      ← Phase 6 新增
    └── 系  统
        ├── 🔧 参数配置
        └── 👤 用户管理
```

总计：**19 个功能页面**

---

## 六、测试覆盖

| 测试类 | 用例数 | 覆盖模块 |
|--------|--------|----------|
| EventAggregatorTests | 5 | EventAggregator |
| AlarmServiceTests | 7 | AlarmService |
| UserServiceTests | 8 | UserService |
| DeviceStateMachineTests | 11 | DeviceStateMachine |
| RecipeServiceTests | 6 | RecipeService |
| TraceabilityServiceTests | 3 | TraceabilityService |
| SafetyInterlockTests | 9 | SafetyInterlockService |
| **合计** | **49** | **7 个核心模块** |

全部通过，0 失败。

---

## 七、构建状态

```
dotnet build SmartHMI.sln
→ 已成功生成。0 个警告，0 个错误

dotnet test SmartHMI.Tests
→ 已通过! 失败: 0，通过: 49，总计: 49
```
