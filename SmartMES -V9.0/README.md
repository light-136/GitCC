# SmartMES 智能制造上位机系统

> 版本 v3.0.0 | .NET 9 + WPF | 工业级架构

---

## 快速启动

```powershell
# 1. 构建
cd 'D:\软件\Cursor\文档存放\代码区\SmartMES'
dotnet build SmartMES.sln

# 2. 运行
dotnet run --project SmartMES.UI
```

## 内置账号

| 用户名 | 密码 | 角色 |
|--------|------|------|
| admin | admin123 | 管理员（全部权限）|
| operator | op123 | 操作员 |
| viewer | view123 | 观察者（只读）|

## 系统功能

```
监控模块:    仪表盘 / 设备管理 / 通信监控 / 报警 / 日志 / 自动化流程
扩展模块:    MES通信 / 文件处理 / 数据库 / 运动控制(3轴) / C++ Native
样例演示:    10轴G代码 / 视觉检测 / 视觉+运动协同调度
工业级能力:  调度系统 / 状态机 / IO系统 / 配方 / 安全互锁 / 追溯 / 插件化
系统管理:    用户权限(16页面) / 参数配置
```

## 项目结构

```
SmartMES/
├── SmartMES.Core/          基础层
│   ├── Scheduler/          任务调度中心
│   ├── StateMachine/       状态机引擎
│   ├── IO/                 IO控制
│   ├── Recipe/             配方系统
│   ├── Safety/             安全互锁
│   ├── Traceability/       追溯系统
│   └── Plugin/             插件化架构
├── SmartMES.Modules/       业务模块层
│   ├── MotionControl/      运动控制
│   ├── Vision/             视觉处理
│   ├── VisionMotion/       视觉运动协同
│   ├── Database/           数据库
│   └── FileProcess/        文件处理
├── SmartMES.Services/      服务层
├── SmartMES.UI/            WPF表现层
│   └── Modules/            各功能页面
└── docs/                   项目文档
```

## 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET / WPF | 9.0-windows | 运行时+UI |
| EF Core | 9.0.5 | 多数据库ORM |
| EPPlus | 7.7.0 | Excel读写 |
| Dapper | 2.1.66 | 轻量SQL |

## 文档

- [详细设计文档](docs/01_详细设计文档.md)
- [项目规程报告 v2](docs/02_项目规程报告.md)
- [快速启动指南](docs/03_快速启动指南.md)
- [项目规程报告 v3](docs/04_v3规程报告.md)
- [v3.1.2 执行开发文档](docs/11_v3.1.2_执行开发文档.md)
