# SmartHMI 项目修复与完善开发规划

> 文档类型：开发规划文件  
> 生成时间：2026-04-27  
> 执行人：AI 开发助手  
> 项目路径：`d:/软件/Cursor/文档存放/代码区/SmartHMI-Learn/`

---

## 一、问题分析（规则12 — 推理思路）

### 1.1 启动崩溃根因

| 编号 | 问题描述 | 根因 | 修复方案 |
|------|----------|------|----------|
| BUG-01 | 启动时 XamlParseException，找不到 MainWindow 默认构造函数 | `App.xaml` 中 `StartupUri="MainWindow.xaml"` 让 WPF 用无参构造函数实例化 MainWindow，但 MainWindow 只有带参构造函数 | 移除 `StartupUri`，改为在 `OnStartup` 中手动创建并显示窗口 |
| BUG-02 | MVVM 不纯粹：MainWindow.xaml.cs 中直接 `new DashboardView()` 并手动设置 DataContext | 违反 MVVM 原则，View 不应知道 ViewModel 的创建方式 | 使用 DataTemplate 自动映射 ViewModel → View |
| BUG-03 | DashboardView 中 `Converters.BoolToColor` 静态引用方式不正确 | WPF 不支持 `{x:Static}` 直接引用返回 Color 的转换器 | 改为资源字典注册方式 |
| BUG-04 | LoginView 密码框通过 code-behind 手动绑定，不符合 MVVM | PasswordBox 无法直接绑定，需要附加属性或行为 | 使用 PasswordBoxHelper 附加属性实现 MVVM 绑定 |

### 1.2 MVVM 架构完善需求

当前项目 MVVM 不完整，需要：
1. **ViewLocator（视图定位器）**：自动根据 ViewModel 类型找到对应 View
2. **DataTemplate 映射**：在 App.xaml 中注册 ViewModel → View 的映射
3. **导航服务（INavigationService）**：ViewModel 层控制导航，不依赖 View
4. **PasswordBoxHelper**：解决 PasswordBox 无法绑定问题
5. **消息对话框服务（IDialogService）**：ViewModel 弹窗不依赖 View

---

## 二、开发规划

### 阶段一：修复启动崩溃（P0 — 立即执行）

- [ ] 修复 App.xaml，移除 StartupUri
- [ ] 修复 App.xaml.cs，OnStartup 中手动创建主窗口
- [ ] 添加 PasswordBoxHelper 附加属性
- [ ] 修复 DashboardView 转换器引用

### 阶段二：MVVM 架构完善（P1）

- [ ] 新增 `INavigationService` 接口 + `NavigationService` 实现
- [ ] 新增 `ViewLocator`（DataTemplate 自动映射）
- [ ] 在 App.xaml 注册所有 ViewModel → View 的 DataTemplate
- [ ] MainWindow 改为纯 MVVM：ContentControl 绑定 CurrentViewModel
- [ ] MainViewModel 持有 CurrentViewModel 属性，导航时切换
- [ ] 移除 MainWindow.xaml.cs 中所有 `new XxxView()` 代码

### 阶段三：功能完善（P2）

- [ ] 仪表盘：模拟报警触发按钮（演示用）
- [ ] 报警服务：定时触发模拟报警
- [ ] 日志服务：启动时写入初始化日志
- [ ] 通信页：连接状态实时刷新

### 阶段四：文档补齐（P3 — 规则11）

- [ ] 详细设计文档
- [ ] 项目规程报告
- [ ] 快速启动指南
- [ ] 测试报告
- [ ] 软件使用说明
- [ ] 完整工程文档

---

## 三、MVVM 架构设计说明

```
┌─────────────────────────────────────────────────────┐
│                    SmartHMI.UI                       │
│                                                      │
│  App.xaml                                            │
│  ├── DataTemplate: LoginViewModel → LoginView        │
│  ├── DataTemplate: DashboardViewModel → DashboardView│
│  ├── DataTemplate: DeviceViewModel → DeviceView      │
│  └── ... (所有 ViewModel → View 映射)                │
│                                                      │
│  MainWindow.xaml                                     │
│  └── ContentControl Content="{Binding CurrentVM}"   │
│       ↑ WPF 自动根据 DataTemplate 选择对应 View      │
│                                                      │
│  MainViewModel                                       │
│  ├── CurrentViewModel: BaseViewModel (导航目标)      │
│  ├── NavigateCommand → 切换 CurrentViewModel         │
│  └── 注入 INavigationService                         │
│                                                      │
│  INavigationService                                  │
│  └── NavigateTo<TViewModel>()                        │
└─────────────────────────────────────────────────────┘
```

---

## 四、文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| App.xaml | 修改 | 移除 StartupUri，添加 DataTemplate 映射 |
| App.xaml.cs | 修改 | OnStartup 手动创建窗口 |
| MainWindow.xaml | 修改 | ContentControl 绑定 CurrentViewModel |
| MainWindow.xaml.cs | 修改 | 移除手动导航代码 |
| MainViewModel.cs | 修改 | 添加 CurrentViewModel 属性和导航逻辑 |
| INavigationService.cs | 新增 | 导航服务接口 |
| NavigationService.cs | 新增 | 导航服务实现 |
| PasswordBoxHelper.cs | 新增 | PasswordBox MVVM 绑定附加属性 |
| LoginView.xaml | 修改 | 使用 PasswordBoxHelper 替代 code-behind |
| DashboardView.xaml | 修改 | 修复转换器引用 |
| Views/LoginView.xaml.cs | 修改 | 移除密码绑定代码 |

---

*规划完成，开始执行修复。*
