# SmartHMI 第六阶段开发计划报告

**日期：** 2026-04-27  
**阶段：** Phase 6 — 云端同步 / SECS-GEM / AI 助手 / AI 视觉扩展

---

## 一、现状盘点

### 已完成（第1-5阶段）

| 阶段 | 模块 | 状态 |
|------|------|------|
| 1 | 项目骨架、DI、MVVM、主题、导航 | ✅ 完整 |
| 1 | 基础文档（设计/使用/测试报告） | ✅ 完整 |
| 2 | TCP/Serial/MQTT/OPC UA 通信管理 | ✅ 完整 |
| 2 | 设备管理（8台仿真设备） | ✅ 完整 |
| 2 | 状态机（DeviceStateMachine） | ✅ 完整 |
| 3 | 安全互锁（SafetyInterlock） | ⚠️ 缺失 |
| 3 | 单轴控制（AxisController） | ✅ 完整 |
| 3 | 程序执行（ProgramExecutor） | ⚠️ 缺失 |
| 4 | 多轴控制（MotionManager X/Y/Z） | ✅ 完整 |
| 4 | 视觉基础链路（VisionService） | ⚠️ 缺失 |
| 4 | 视觉运动协同（VisionMotionCoord） | ⚠️ 缺失 |
| 5 | 配方系统（RecipeService） | ⚠️ 缺失 |
| 5 | 数据层（AppDbContext） | ✅ 基础完整 |
| 5 | 追溯系统（TraceabilityService） | ⚠️ 缺失 |
| 5 | 报表系统（ReportService） | ⚠️ 缺失 |
| 5 | MES 协同（MesConnector） | ⚠️ 缺失 |

### 缺失模块（需补全）

共 **8 个** 缺失模块需要在本次开发中补全，然后再进入第六阶段。

---

## 二、本次开发范围

### 补全第3-5阶段缺失模块

1. **SafetyInterlockService** — 安全互锁引擎（条件检查、互锁触发、急停联动）
2. **ProgramExecutor** — 程序执行引擎（步骤序列、条件跳转、循环）
3. **VisionService** — 视觉基础链路（相机仿真、图像采集、结果解析）
4. **VisionMotionCoordinator** — 视觉运动协同（视觉引导定位）
5. **RecipeService** — 配方系统（配方增删改查、参数下发）
6. **TraceabilityService** — 追溯系统（工件追踪、工序记录）
7. **ReportService** — 报表系统（EPPlus 导出 Excel）
8. **MesConnector** — MES 协同（REST API 对接、工单同步）

### 第六阶段新模块

9. **CloudSyncService** — 云端同步（HTTP REST 上传设备数据/报警/日志）
10. **SecsGemService** — SECS/GEM 抽象层（S1F1 心跳、S6F11 事件上报、S2F41 命令）
11. **AiAssistantService** — AI 助手（本地规则引擎 + 可选 LLM API 接入）
12. **VisionAnalysisService** — AI 视觉分析扩展（缺陷检测仿真、置信度评分）

---

## 三、文件新增清单

### SmartHMI.Core 新增

```
Models/
  RecipeModel.cs          — 配方数据模型
  TraceRecord.cs          — 追溯记录模型
  VisionResult.cs         — 视觉检测结果模型
  SecsGemMessage.cs       — SECS/GEM 消息模型
  CloudSyncRecord.cs      — 云同步记录模型
  AiSuggestion.cs         — AI 建议模型
Interfaces/
  IRecipeService.cs
  ITraceabilityService.cs
  IReportService.cs
  IMesConnector.cs
  ICloudSyncService.cs
  ISecsGemService.cs
  IAiAssistantService.cs
  IVisionService.cs
Events/
  AppEvents.cs            — 追加新事件（RecipeApplied, VisionResult, AiSuggestion等）
```

### SmartHMI.Modules 新增

```
Safety/
  SafetyInterlockService.cs
Program/
  ProgramExecutor.cs
  ProgramStep.cs
Vision/
  VisionService.cs
  VisionMotionCoordinator.cs
  VisionAnalysisService.cs
Recipe/
  RecipeService.cs
Traceability/
  TraceabilityService.cs
Report/
  ReportService.cs
Mes/
  MesConnector.cs
Cloud/
  CloudSyncService.cs
SecsGem/
  SecsGemService.cs
Ai/
  AiAssistantService.cs
```

### SmartHMI.UI 新增

```
ViewModels/
  SafetyViewModel.cs
  ProgramViewModel.cs
  VisionViewModel.cs
  RecipeViewModel.cs
  TraceabilityViewModel.cs
  ReportViewModel.cs
  MesViewModel.cs
  CloudSyncViewModel.cs
  SecsGemViewModel.cs
  AiAssistantViewModel.cs
Views/
  SafetyView.xaml + .cs
  ProgramView.xaml + .cs
  VisionView.xaml + .cs
  RecipeView.xaml + .cs
  TraceabilityView.xaml + .cs
  ReportView.xaml + .cs
  MesView.xaml + .cs
  CloudSyncView.xaml + .cs
  SecsGemView.xaml + .cs
  AiAssistantView.xaml + .cs
```

### 文档新增

```
docs/
  SmartHMI_Phase3_安全互锁与程序执行设计.md
  SmartHMI_Phase4_视觉系统设计.md
  SmartHMI_Phase5_配方追溯报表MES设计.md
  SmartHMI_Phase6_云端同步SECSAI设计.md
```

---

## 四、开发顺序

```
Step 1: Core 层 — 新增模型 + 接口 + 事件
Step 2: Modules 层 — 安全互锁、程序执行、视觉
Step 3: Modules 层 — 配方、追溯、报表、MES
Step 4: Modules 层 — 云同步、SECS/GEM、AI助手、AI视觉
Step 5: UI 层 — 所有新 ViewModel + View
Step 6: DI 注册 + 导航注册 + App.xaml DataTemplate
Step 7: 单元测试补充
Step 8: 全量构建验证
Step 9: 文档输出
```

---

## 五、技术选型

| 功能 | 技术方案 |
|------|----------|
| 云端同步 | System.Net.Http.HttpClient（REST JSON） |
| SECS/GEM | 自实现抽象层（仿真模式，不依赖第三方库） |
| AI 助手 | 本地规则引擎 + HttpClient 调用 OpenAI 兼容 API |
| AI 视觉 | 仿真检测结果（可替换为 ONNX Runtime 真实推理） |
| 报表导出 | EPPlus（已在 Modules.csproj 中引用） |
| MES 对接 | HttpClient REST（仿真模式） |

---

## 六、预期成果

- 完整的 12 个新模块（后端 + UI）
- 导航菜单扩展至 19 个页面
- 单元测试覆盖新增核心服务
- 4 份阶段设计文档
- 全量构建 0 错误 0 警告
