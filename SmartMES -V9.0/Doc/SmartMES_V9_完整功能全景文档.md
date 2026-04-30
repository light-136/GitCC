# SmartMES V9.0 完整功能全景文档

**版本：** V9.0+ 最终版  
**日期：** 2026-04-30  
**总代码量：** ~40,000行 C# + ~25个文档  
**测试覆盖：** 225个单元测试（220通过，5跳过，0失败）

---

## 一、项目全景

SmartMES V9.0 是一套面向半导体、精密制造行业的**工业上位机仿真平台**，涵盖：

```
┌────────────────────────────────────────────────────────────┐
│                    SmartMES V9.0 功能矩阵                    │
├────────────┬───────────────────────────────────────────────┤
│ 运动控制    │ 单轴/10轴/30轴、S曲线、圆弧插补、前瞻规划     │
│            │ 电子齿轮/凸轮、G代码解析执行、坐标系变换        │
│            │ 30轴工业级（6轴组/6通道/碰撞检测/性能监控）      │
├────────────┼───────────────────────────────────────────────┤
│ 视觉检测    │ 图像处理管线、NCC模板匹配、Blob分析、形态学     │
│            │ 直方图分析、OTSU阈值、测量工具、9点标定          │
├────────────┼───────────────────────────────────────────────┤
│ SECS/GEM   │ HSMS传输层、SECS-II编解码、GEM双层状态机       │
│            │ SV/EC/CE管理、告警、远程命令、工艺程序管理       │
├────────────┼───────────────────────────────────────────────┤
│ MES对接     │ HTTP/MQTT/OPC-UA客户端、Modbus TCP协议         │
├────────────┼───────────────────────────────────────────────┤
│ 基础设施    │ 配方版本管理、配置中心、报表服务、报警服务       │
│            │ 日志服务、事件总线、安全互锁、状态机引擎         │
│            │ 任务调度、IO仿真、生产追溯、用户权限管理         │
├────────────┼───────────────────────────────────────────────┤
│ 用户界面    │ WPF/MVVM仪表盘、设备管理、通信监控、运动面板    │
│            │ 视觉面板、报警面板、报表导出、权限管理           │
└────────────┴───────────────────────────────────────────────┘
```

---

## 二、技术架构

### 2.1 技术栈
| 项 | 选型 |
|----|------|
| 运行时 | .NET 9.0 (Windows) |
| UI框架 | WPF + XAML |
| 架构模式 | MVVM (ViewModelBase + RelayCommand) |
| 语言 | C# 13 + C++/CLI (Native) |
| 测试 | xUnit 2.9.3 |

### 2.2 分层架构
```
SmartMES.UI          → WPF界面层（ViewModels、Views、Resources）
SmartMES.Modules     → 功能模块层（运动控制17文件/视觉11文件/MES/数据库）
SmartMES.Services    → 服务层（通信/SECS-GEM 10文件/报警/日志/事件/报表/配置）
SmartMES.Core        → 核心层（接口/模型/状态机/安全/配方/追溯/调度）
SmartMES.Native      → 原生层（C++ 高性能计算）
SmartMES.Tests       → 单元测试（25个测试类，225个测试用例）
```

### 2.3 设计原则
1. **仿真优先** — 所有模块无硬件即可运行
2. **接口驱动** — Core层定义接口，上层实现
3. **事件解耦** — IEventBus 发布/订阅
4. **线程安全** — lock/Interlocked 保护共享状态
5. **中文注释** — 全部代码注释使用中文

---

## 三、运动控制系统

### 3.1 模块概览（17个文件，5,640行）

| 层级 | 文件 | 行数 | 功能 |
|------|------|------|------|
| 基础 | AxisController.cs | 299 | 单轴状态机+梯形运动仿真 |
| 基础 | MultiAxisController.cs | 95 | 多轴容器（字典存储） |
| 10轴 | TenAxisController.cs | 211 | 10轴G代码控制器 |
| 10轴V2 | TenAxisControllerV2.cs | 499 | 集成高级运动模块 |
| 高级 | SCurveProfile.cs | 141 | 7段S曲线运动规划 |
| 高级 | TrapezoidalProfile.cs | 208 | 梯形运动规划（IMotionProfile） |
| 高级 | CircularInterpolator.cs | 207 | G2/G3圆弧插补 |
| 高级 | LookAheadPlanner.cs | 160 | 三遍前瞻速度规划 |
| 高级 | ElectronicGearing.cs | 250 | 电子齿轮+凸轮 |
| 高级 | CoordinateManager.cs | 125 | G54~G59坐标系变换 |
| 高级 | HomingService.cs | 319 | 4策略回零 |
| 高级 | GCodeParserV2.cs | 480 | 扩展G代码解析器 |
| **30轴** | **ThirtyAxisController.cs** | **538** | **顶层编排器（门面模式）** |
| **30轴** | **AxisGroupManager.cs** | **440** | **6轴组管理+龙门同步** |
| **30轴** | **MultiChannelController.cs** | **727** | **6通道G代码并行执行** |
| **30轴** | **CollisionDetector.cs** | **475** | **碰撞检测+安全互锁** |
| **30轴** | **PerformanceMonitor.cs** | **466** | **性能采样+诊断报告** |

### 3.2 30轴配置

| 轴组 | 轴数 | 通道 | 用途 |
|------|------|------|------|
| Gantry1 (主龙门) | X1,Y1,Z1,A1,B1 (5轴) | CH0 | 主加工头 |
| Gantry2 (副龙门) | X2,Y2,Z2,A2,B2 (5轴) | CH1 | 副加工头 |
| Robot (机械臂) | J1~J6 (6轴) | CH2 | 物料搬运 |
| Conveyor (传送带) | C1~C4 (4轴) | CH3 | 物料输送 |
| Spindle (主轴) | S1~S4 (4轴) | CH4 | 旋转加工 |
| Auxiliary (辅助) | AUX1~AUX6 (6轴) | CH5 | 定位/夹持/翻转 |

### 3.3 核心算法

- **S曲线**：7段加加速度限制运动，短距离自动退化
- **圆弧插补**：I/J圆心偏移法 + R半径法，微线段生成
- **前瞻规划**：三遍算法（前向拐角→后向减速→前向加速），确保速度连续
- **碰撞检测**：50ms周期后台扫描，Critical级别自动急停
- **性能监控**：100ms采样，跟随误差/速度/负载/周期时间

---

## 四、SECS/GEM 半导体通信

### 4.1 模块概览（10个文件，3,455行）

| 文件 | 行数 | SEMI标准 | 功能 |
|------|------|---------|------|
| HsmsMessage.cs | 177 | E37 | 报文头编解码 |
| HsmsConnection.cs | 460 | E37 | TCP传输+状态机+定时器 |
| SecsIICodec.cs | 429 | E5 | 递归SecsItem编解码 |
| GemStateMachine.cs | 274 | E30 | 通信+控制双层状态机 |
| GemDataManager.cs | 361 | E30 | SV/EC/CE/报告管理 |
| GemAlarmManager.cs | 271 | E30 | 告警管理(S5F1/5/7) |
| GemRemoteCommandHandler.cs | 259 | E30 | 远程命令(S2F41/42) |
| GemProcessProgramManager.cs | 272 | E30 | 工艺程序(S7F1~20) |
| SecsGemService.cs | 569 | 综合 | 顶层编排服务 |
| SecsGemHostSimulator.cs | 393 | 测试 | 模拟GEM主机 |

### 4.2 实现的消息

| Stream | Function | 方向 | 功能 |
|--------|----------|------|------|
| S1 | F1/F2 | Host↔Eqp | 在线确认 |
| S1 | F3/F4 | Host→Eqp | 查询状态变量 |
| S1 | F13/F14 | Eqp→Host | 在线请求 |
| S1 | F15/F16 | Host→Eqp | 离线请求 |
| S2 | F13/F14 | Host→Eqp | 查询设备常量 |
| S2 | F15/F16 | Host→Eqp | 设置设备常量 |
| S2 | F33/F34 | Host→Eqp | 定义报告 |
| S2 | F35/F36 | Host→Eqp | 链接事件与报告 |
| S2 | F37/F38 | Host→Eqp | 启用/禁用事件 |
| S2 | F41/F42 | Host→Eqp | 远程命令 |
| S5 | F1/F2 | Eqp→Host | 告警报告 |
| S5 | F5/F6 | Host→Eqp | 告警列表 |
| S6 | F11/F12 | Eqp→Host | 事件报告 |
| S7 | F1/F2 | Host→Eqp | PP加载请求 |
| S7 | F3/F4 | Host→Eqp | PP发送 |
| S7 | F5/F6 | Host→Eqp | PP请求 |
| S7 | F17/F18 | Host→Eqp | PP删除 |
| S7 | F19/F20 | Host→Eqp | PP目录 |

---

## 五、视觉检测系统

### 5.1 模块概览（11个文件，2,552行）

| 文件 | 行数 | 功能 |
|------|------|------|
| ImageProcessor.cs | 239 | 灰度化/高斯模糊/中值滤波/Sobel/阈值 |
| MorphologyProcessor.cs | 183 | 腐蚀/膨胀/开/闭/顶帽/黑帽 |
| HistogramAnalyzer.cs | 188 | OTSU阈值/直方图均衡化/自适应阈值 |
| TemplateMatcher.cs | 318 | NCC归一化互相关+金字塔+NMS |
| BlobAnalyzer.cs | 262 | 两遍CCL+并查集+特征提取 |
| MeasurementTools.cs | 210 | 距离/角度/圆拟合(Kasa)/直线拟合 |
| CalibrationService.cs | 266 | 9点仿射标定+最小二乘 |
| VisionPipeline.cs | 161 | 可配置步骤链+中间诊断 |
| CameraService.cs | 231 | 模拟相机+工厂模式 |
| VisionEngineV2.cs | 263 | V2门面（组合子系统） |
| VisionEngine.cs | 231 | V1引擎（WPF直接处理） |

### 5.2 核心算法

- **NCC模板匹配**：归一化互相关系数，积分图加速，金字塔粗搜+精定位
- **Blob分析**：两遍扫描连通域标记 + Union-Find路径压缩 + 面积/重心/外接矩形
- **OTSU阈值**：遍历0~255，最大化类间方差
- **Kasa圆拟合**：代数拟合法，精度 <0.01像素
- **9点标定**：最小二乘求解2×3仿射矩阵

---

## 六、MES/通信模块

| 模块 | 协议 | 文件 | 功能 |
|------|------|------|------|
| TCP通信 | TCP | TcpCommunicationService.cs | 仿真TCP客户端 |
| 串口通信 | Serial | SerialCommunicationService.cs | 仿真串口 |
| Modbus | Modbus TCP | ModbusTcpService.cs | 真实Modbus TCP协议 |
| MES HTTP | REST | MesHttpClient.cs | REST API对接 |
| MES MQTT | MQTT | MesMqttClient.cs | 消息队列对接 |
| MES OPC | OPC-UA | OpcUaClient.cs | 工业协议对接 |

---

## 七、基础设施模块

| 模块 | 功能 | 关键特性 |
|------|------|---------|
| 配方管理 | 版本控制+审批流 | Draft→Active→Archived 状态机 |
| 配置中心 | 统一配置管理 | JSON持久化/多环境/链式API/校验 |
| 报表服务 | 生产报表+KPI | 产量/报警统计/OEE/CSV导出 |
| 报警服务 | 实时报警 | 触发/确认/历史/级别过滤 |
| 日志服务 | 多级别日志 | Debug/Info/Warning/Error |
| 事件总线 | 模块解耦 | 发布/订阅模式 |
| 安全互锁 | 设备安全 | 条件检查/急停/互锁规则 |
| 状态机引擎 | 通用FSM | 状态/触发/守卫/转换事件 |
| 任务调度 | 定时任务 | 单次/周期/异常处理 |
| IO仿真 | 数字/模拟IO | 输入/输出/模拟值读写 |
| 追溯服务 | 生产追溯 | 工序记录/质量数据 |
| 用户权限 | 角色管理 | 登录/角色/页面权限控制 |

---

## 八、代码统计

### 8.1 按项目分类

| 项目 | 文件数 | 行数（估算） |
|------|--------|-------------|
| SmartMES.Core | ~25 | ~4,500 |
| SmartMES.Services | ~20 | ~6,500 |
| SmartMES.Modules | ~40 | ~10,000 |
| SmartMES.UI | ~30 | ~8,000 |
| SmartMES.Native | ~5 | ~1,500 |
| SmartMES.Tests | ~25 | ~7,000 |
| **合计** | **~145** | **~37,500** |

### 8.2 按模块分类

| 模块 | 文件数 | 行数 |
|------|--------|------|
| 运动控制 | 17 | 5,640 |
| SECS/GEM | 10 | 3,455 |
| 视觉检测 | 11 | 2,552 |
| 核心模型+接口 | 8 | 2,200 |
| 测试 | 25 | 7,000 |

---

## 九、测试矩阵

### 9.1 总体结果

```
总计: 225 个测试用例
通过: 220 个
跳过:   5 个（需真实TCP环境）
失败:   0 个
```

### 9.2 模块覆盖

| 模块 | 测试数 | 行覆盖率（估算） |
|------|--------|-----------------|
| 配方版本管理 | 34 | ≥85% |
| 配置中心 | 31 | ≥90% |
| 报表服务 | 38 | ≥95% |
| Modbus通信 | 22 | ≥60% |
| S曲线 | 5 | ≥85% |
| 圆弧插补 | 4 | ≥80% |
| 前瞻规划 | 4 | ≥75% |
| G代码解析 | 6 | ≥80% |
| 坐标系管理 | 4 | ≥85% |
| SECS-II编解码 | 8 | ≥85% |
| GEM状态机 | 8 | ≥80% |
| 视觉引擎V2 | 12 | ≥80% |
| 30轴控制 | 15 | ≥75% |

---

## 十、发现并修复的缺陷（共12个）

| # | 缺陷 | 严重度 | 文件 | 修复 |
|---|------|--------|------|------|
| 1 | AlarmService.cs GBK 乱码 | 中 | AlarmService.cs | 重写为 UTF-8 |
| 2 | StateMachineEngine.cs GBK 乱码 | 中 | StateMachineEngine.cs | 重写为 UTF-8 |
| 3 | App.xaml.cs 异常吞没 | 高 | App.xaml.cs | 添加 LogWarning |
| 4 | AxisController.MoveTo() 死锁风险 | 高 | AxisController.cs | 分离锁顺序 |
| 5 | ReportService.cs 缺少 using | 中 | ReportService.cs | 添加 using |
| 6 | ReportViewModel.cs 缺少 using | 中 | ReportViewModel.cs | 添加 using |
| 7 | NCC模板匹配分母错误 | 高 | TemplateMatcher.cs | 修正公式 |
| 8 | TemplateMatcher 线程不安全 | 中 | TemplateMatcher.cs | 局部变量替代属性修改 |
| 9 | VisionEngine 评分负值 | 中 | VisionEngine.cs | Math.Max(0,...) |
| 10 | RunGCodeAsync 取消挂起 | 高 | TenAxisController.cs | 循环内检查取消标记 |
| 11 | 测试断言 GBK 乱码 | 中 | TenAxisControllerTests.cs | 重写 UTF-8 |
| 12 | SecsGemService 缺 S7F1/3/5/17 | 中 | SecsGemService.cs | 添加消息处理 |

---

## 十一、文档清单

### Doc/ 目录（核心设计文档）

| 文档 | 用途 |
|------|------|
| 项目概要说明.md | 架构总览、技术栈、模块依赖 |
| 软件使用说明.md | 用户操作手册 |
| 软件流程使用说明.md | 业务流程指南 |
| 高级运动控制设计文档.md | S曲线/圆弧/前瞻/齿轮算法 |
| 30轴运动控制系统设计文档.md | 30轴架构/配置/子模块设计 |
| SECS_GEM_设计文档.md | HSMS/SECS-II/GEM协议设计 |
| 视觉系统设计文档.md | NCC/CCL/OTSU/标定算法 |
| SmartMES_V9_详细设计文档.md | 系统详细设计 |
| SmartMES_V9_项目规程报告.md | 项目管理规程 |
| SmartMES_V9_快速启动指南.md | 环境搭建与运行 |
| SmartMES_V9_测试报告.md | 225个测试详细结果 |
| SmartMES_V9_完整功能全景文档.md | 本文档 |

### docs/ 目录（扩展学习文档）

| 文档 | 用途 |
|------|------|
| 01~06 | 设计/规程/启动/测试/测试设计 |
| 09_工程功能分级与函数职责说明 | 模块划分详解 |
| 14_后续开发路线图 | 未来规划 |
| 15_完善版工业上位机完整设计文档 | 全面设计 |
| 16_工业上位机需求规格说明书 | 功能需求 |
| 17~24 | 工业上位机知识/学习文档 |

---

## 十二、学习路线建议

### 入门路线（基础理解）
1. `项目概要说明.md` → 了解整体架构
2. `快速启动指南.md` → 搭建开发环境
3. `SmartMES.Core/` → 阅读接口和模型定义
4. `AxisController.cs` → 理解单轴状态机

### 进阶路线（核心算法）
5. `SCurveProfile.cs` → S曲线运动规划数学
6. `CircularInterpolator.cs` → 圆弧插补几何
7. `LookAheadPlanner.cs` → 前瞻三遍算法
8. `TemplateMatcher.cs` → NCC模板匹配
9. `BlobAnalyzer.cs` → 连通域分析

### 高级路线（工业协议）
10. `SecsIICodec.cs` → SECS-II二进制编解码
11. `GemStateMachine.cs` → GEM双层状态机
12. `HsmsConnection.cs` → HSMS TCP传输

### 系统级路线（30轴工业级）
13. `ThirtyAxisController.cs` → 30轴编排
14. `AxisGroupManager.cs` → 轴组管理
15. `MultiChannelController.cs` → 多通道并行
16. `CollisionDetector.cs` → 碰撞检测
17. `PerformanceMonitor.cs` → 性能监控

---

*SmartMES V9.0 完整功能全景文档 | 2026-04-30*
