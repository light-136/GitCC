# DataScope Studio 文档索引

> 本文档是项目文档体系的入口导航。所有文档均为简体中文，markdown 格式。

## 文档体系结构

```
docs/
├── README.md                         ← 本文档（导航总览）
├── 00-项目规程报告.md                ← 项目规程：阶段划分、Agent 协作、质量规范
├── 01-项目概述与需求文档.md          ← 需求规格（功能/非功能需求，编号 FR-xx）
├── 02-总体设计文档.md                ← 架构分层、模块划分、数据流、线程模型
├── 03-详细设计文档.md                ← 类级设计：核心类、方法签名、状态机
├── 04-阶段开发记录.md                ← P1~P15 开发历程：提交时间线、阶段映射、测试演进
├── 05-构建与部署说明.md              ← 环境搭建、构建命令、发布方法、排错
├── 06-测试文档.md                    ← 21 个测试套件逐一说明与测试技术（V2 后）
├── 07-使用说明.md                    ← 面向最终用户的软件操作手册
├── 08-项目总结.md                    ← 开发复盘、技术认知、质量评估
├── 09-DataScope Studio 自主开发最终报告.md  ← 最终交付报告
├── 10-Agent 协作报告.md              ← 多 Agent 协作模式总结
├── 11-DataScope Studio V2 工程质量提升计划.md  ← V2 重构目标蓝图与执行路线
├── learn/                            ← Qt 学习体系（面向 WPF 背景开发者）
│   ├── README.md                     ← 学习导航：Level 1-7 路径图
│   ├── wpf-qt-mapping.md             ← WPF→Qt 14 概念深入映射
│   ├── architecture-walkthrough.md   ← 架构导览/源码行走（一条数据流贯穿）
│   ├── qt-concepts.md                ← 工程问题驱动的 Qt 概念精讲
│   └── exercises.md                  ← Level 1-7 阶段练习 + 验收 + 答案索引
├── reviews/                          ← 审查与 V2 执行验证报告
│   ├── 01-DataScope V1 严格审查报告.md
│   ├── 03-V2 执行③ 模拟设备与测试体系验证.md
│   └── 04-V2 执行④ 教学体系与文档重构.md
├── contracts/                        ← 契约文档（源码注释引用）
│   ├── 03-通信协议帧格式契约.md      ← 帧格式 / 字节序 / CRC 约定
│   ├── 05-TCP链路与模拟设备契约.md   ← 连接约定 / 接口 / 模拟设备行为
│   ├── 06-采集链路与数据总线契约.md  ← 帧→数据转换 / 数据总线信号
│   └── 07-设备状态机契约.md          ← 状态转移表 / 退避重连策略
└── stages/                           ← 阶段学习文档（P1~P15）
    ├── P01.md ... P15.md
```

## 编号体系说明

- **根目录 00~11**：项目文档体系编号（规程→需求→设计→构建→测试→使用→总结→报告→协作→V2计划）。
- **contracts/ 子目录**：契约文档使用**源码注释中的引用编号**（《03》《05》《06》《07》）。
  源码注释形如「严格对照《03-通信协议帧格式契约.md》」，指向本目录同名文件。
  两套编号是不同文档集，请以**所在目录**区分。
- **stages/ 子目录**：每阶段一份学习文档，编号 P01~P15，含阶段目标、迁移锚点、实现、测试、难点。
- **learn/ 子目录**：Qt 学习体系，面向 WPF 背景开发者——WPF→Qt 映射、架构导览、概念精讲、阶段练习。

## 建议阅读顺序

1. 新接触项目 → `00-项目规程报告` + `01-项目概述与需求文档`
2. 理解架构 → `02-总体设计文档` → `03-详细设计文档`
3. 查看实现细节 → `contracts/` 契约文档（与源码注释互相对应）
4. 构建与验证 → `05-构建与部署说明` + `06-测试文档`
5. 使用软件 → `07-使用说明`
6. 总结与交付 → `08-项目总结` + `09-最终报告` + `10-Agent 协作报告`

## Qt 学习路径（面向 WPF 背景）

→ 想学 Qt / 读本项目源码：进 [`learn/README.md`](./learn/README.md)（Level 1-7 路径图）。
学习文档与工程文档互相印证（源码 ↔ 契约 ↔ 学习 三向对应见下节）。

## 源码 ↔ 文档 ↔ 学习 三向对应

| 源码位置 | 工程文档 | 学习文档（learn/） |
|----------|----------|--------------------|
| `src/protocol/` | contracts/03 | qt-concepts 第 6 节（协议状态机） |
| `src/services/tcpclient.*`、`src/simulator/` | contracts/05 | exercises L6（异常注入） |
| `src/services/acquisitionworker.*`、`dataservice.*`、`src/domain/` | contracts/06 | architecture 数据流 + qt-concepts 第 3 节（跨线程） |
| `src/services/devicecontroller.*` | contracts/07 | exercises L6（重连状态机） |
| `src/ui/` | 02 / 03 / 07 | architecture 4.7 + wpf-qt-mapping（UI/Model-View） |
| `tests/unittests/` | 06 | exercises 各级答案索引 |
| 全局架构 | 02 / 03 | architecture-walkthrough（main→数据流→分层） |
| Qt 概念（线程/事件循环/元对象） | 02 / 03 | qt-concepts（工程问题驱动） |
| WPF 迁移视角 | 07 / 02 | wpf-qt-mapping（14 概念映射） |
