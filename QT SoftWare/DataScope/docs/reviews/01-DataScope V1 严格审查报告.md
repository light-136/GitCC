# DataScope Studio V1 严格审查报告

> **性质**：V1（P1~P15）全面 Code Review，用户要求"不要虚高评分"
> **方法**：主 Agent 审查 + 4 个专项 Agent 独立审查（UI/交互、架构/C++/Qt、多线程/通信/错误、测试/文档/真实度）
> **日期**：2026-08-15 ｜ **结论**：V1 是"覆盖 Qt 知识点的 Demo 集合"，未达到中型软件标准，需 V2 重构

---

## 一、12 维评分总表（满分 10）

| 项目 | 得分 | 一句话理由 |
|------|------|-----------|
| UI/视觉完成度 | **4.0** | 深色主题统一、自绘控件尚可，但设备/设置页空白、QSS 覆盖不全、无时间轴与报警可视化 |
| 软件交互完整度 | **3.5** | 仅记录/回放页有真交互；无设备配置、无断开入口、无日志/报警面板，专业工作流闭环大面积缺失 |
| 架构合理性 | **4.0** | 分层骨架与依赖方向干净，但两个占位页、死掉的 DeviceController、采集启停语义空洞、Model/View 承诺零实现 |
| C++代码质量 | **7.0** | 现代 C++ 纪律良好（const/RAII/enum class/Q_DISABLE_COPY/TryParse），但 memcpy 位模式、O(n) 移除、hex 状态机晦涩、教学注释过载 |
| Qt使用正确性 | **6.0** | 关键范式对（socket 在线程内创建、moveToThread、对象树回收），但 worker 泄漏、无谓互斥锁、字符串式 invokeMethod、元类型缺注册 |
| Model/View设计 | **1.0** | 承诺的 QAbstractTableModel/TableView 完全缺失（全工程零实现），仅 DTO 可作为未来载体 |
| 多线程设计 | **4.0** | moveToThread 认知正确，但 Worker 泄漏、关停依赖 deleteLater 时序、connectTo 无锁读、disconnected 双发 |
| 通信设计 | **5.0** | 协议解析层健壮（半包/粘包/CRC/LEN 防御），但无连接超时、sendFrame 部分写/线程安全缺失、协议错误零上报、重连未接入 |
| 错误处理 | **3.5** | 错误链完整，但 loadReplay 假装成功、connectTo 二次请求吞没、断线误重连、CRC/协议错误静默丢弃 |
| 测试完整度 | **6.0** | 12 套件 121 断言真实运行全绿、协议层测试扎实，但 Integration/Simulator/Smoke/Deployment/Error 分层为零、无 CI、无 fuzz |
| 文档质量 | **7.0** | 文档体系完整（12+15+4 份）、测试/使用说明诚实，但总结措辞夸大、阶段文档零练习、06 头部版本陈旧 |
| 工程真实度 | **5.0** | TCP/协议/状态机/记录/配置底层真实，但领域模型是死模型、报警在 View、两页占位、配置零消费 |
| **综合** | **约 4.7** | 分层与协议真实的 Demo 集合；离"有生命周期的软件"差：领域补行为、模拟器补注入、UI 去占位、测试补分层 |

> 注：表格由 4 个专项 Agent 独立审查后汇总，评分严格，V1 整体偏低。

---

## 二、审查维度 1：UI / 视觉 / 交互（专项 Agent）

**总体结论**：基础框架能跑、深色主题有整体感、按钮状态机（记录/回放页）是唯一的"真交互"。但"设备管理""设置"两页是纯文字占位，监控页缺时间轴与报警可视化，回放与实时采集数据混叠，教学演示页占据主界面——正是"Demo 集合"而非专业工业监控软件的铁证。

### 问题清单

| 位置 | 严重度 | 问题 | 建议 |
|------|--------|------|------|
| `devicepage.cpp:39-45` | P0 | **设备管理页是纯空壳**：仅静态 QLabel。无设备列表/状态/参数/通道配置/编辑/删除/测试连接 | 真实设备列表(QTableView+DeviceListModel) + 连接参数表单 + CRUD + 测试连接 |
| `settingspage.cpp:39-45` | P0 | **设置页是纯空壳**：ConfigManager 已初始化却未被页面使用，无法修改/保存任何配置 | 接入 ConfigManager 真配置表单，保存即写 INI |
| `linechartwidget.cpp:126-223` | P0 | **监控曲线无时间维度**：timestamp 从未在 UI 使用，曲线无 X 轴时间刻度，只是"索引滚动" | 增加时间轴/网格线，数据缓冲按时间窗管理 |
| `demopage.cpp`、`mainwindow.cpp:153` | P1 | **教学页硬塞主界面**：信号槽演示页与业务页并列 | 正式版移除；如保留置于开发者隐藏模式 |
| `mainwindow.cpp:195` | P1 | **连接参数硬编码**：127.0.0.1:40001 写死，无连接对话框/配置入口 | 连接设置对话框 + 从配置读取 |
| `mainwindow.cpp:78-81`+`recordpage.cpp` | P1 | **回放与实时数据混叠**：replayData 与 dataUpdated 同时驱动同一曲线，回放不可信 | 回放期间暂停实时采集，或独立视图 |
| UI 层全局 | P1 | **无日志/报警区域**：LogManager 存在但 UI 无日志面板/报警列表 | 底部/侧边日志与报警面板 |
| `resources/qss/main.qss` | P1 | **QSS 覆盖不全**：约 130 行，未定义 QGroupBox/QProgressBar/QTableView 等，深色主题下回退系统默认样式 | 补齐所有在用控件，抽设计 token |
| `mainwindow.cpp:122-131` | P2 | **工具栏无图标**：三个裸文字按钮，icons 目录为空 | 补齐图标资源，QAction setIcon |
| `mainwindow.cpp:169` | P2 | **状态栏文案与实际不符**：提示"左侧选页签"实际页签在顶部 | 改正文案 |
| `monitorpage.cpp:199` | P2 | **通道数值缺单位**：只显示 50.0，未拼接单位 | 值标签"数值+单位"，超限标红 |
| `linechartwidget.cpp`+`gaugewidget.cpp` | P2 | **无报警阈值可视化**：LED 变红但曲线无阈值线、仪表无危险区 | 曲线阈值虚线、仪表危险色区 |
| `mainwindow.cpp:63-71` | P2 | **无"断开"入口**：disconnected 槽无 UI 触发 | 工具栏/菜单加"断开设备" |
| `mainwindow.cpp`+`recordpage.cpp` | P1 | **退出无确认**：正在记录 CSV 时直接关闭会丢数据 | closeEvent 检测记录/回放状态 |
| `mainwindow.cpp:47`+`main.cpp` | P3 | **窗口几何硬编码、高 DPI 未处理** | QSettings 保存/恢复，AA_EnableHighDpiScaling |
| `monitorpage.cpp:62-80` | P2 | **监控布局是 4 条等高窄条**：无放大/网格/分栏/聚焦 | 2×2 网格 + 双击最大化 |
| 自绘控件 4 个 | P2 | **颜色/字体硬编码且与 QSS 重复定义**：#16a085 等散落多处 | 抽公共主题常量（色板/字体） |
| `mainwindow.cpp:86` | P3 | **启动即自动连接**：无操作自动连接，失败即闪红 | 显式连接 + 静默提示 |
| `linechartwidget.h:117` | P3 | **曲线时间窗不可配置**：m_maxPoints=500 硬编码 | 支持配置时间窗 |
| `recordpage.cpp` | P3 | **成功反馈缺失**：记录/回放完成无统计反馈 | 补充结果反馈 |

### 评分
- **UI/视觉完成度：4.0/10** —— 深色主题色调统一、自绘控件尚可，但 QSS 覆盖不足、工具栏无图标、监控无时间轴/报警线、数值缺单位、设备与设置页空白。
- **软件交互完整度：3.5/10** —— 记录/回放按钮状态机是唯一亮点；设备与设置零交互、无断开入口、退出无保护、回放数据混叠、无日志/报警面板。

---

## 三、审查维度 2：架构 / C++ / Qt / Model-View（专项 Agent）

**总体结论**：分层命名与依赖方向正确、协议引擎（CRC/组帧/流式解析）质量高、单测体系完整、多线程基本范式（socket 在线程内创建）正确。但"分层架子 + 演示管线"：Model/View 承诺落空、两个占位页交付、状态机控制器为死代码、"采集启停"语义空洞、教学对象编译进主程序。

### 关键事实确认

1. **QAbstractTableModel/ItemModel 完全缺失**：全工程 `src/` 无一处继承，无 QTableView/QListView，仅 devicepage 注释"将来接入 QTableView"占位。规程文档承诺与实现脱节。
2. **P8 名为 Model/View 实为纯 struct DTO**：4 个被动数据结构 + 值语义教学点，无 Qt Model/View 类、无 dataChanged。
3. **AlarmRule/AlarmEvent/DeviceState/AcquisitionRecord 全部不存在**：domain 只有 DeviceStatus/Channel/DataPoint/Device，"告警"这类生命周期概念在设计时就未建模。
4. **DeviceController（P13）在正式程序中是死代码**：MainWindow 直接持有 DataService，状态机/退避重连不生效。TcpClient::sendFrame、DataService::lastData 同样无消费方。

### 问题清单（要点）

| 位置 | 严重度 | 问题 |
|------|--------|------|
| `domain/models.h` | P0 | 领域层只是 4 个被动 DTO，无行为/不变量/生命周期 |
| 全工程 src/ | P0 | Model/View 零实现，两个占位页随"产品"交付 |
| `acquisitionworker.cpp handleFrame` | P1 | 不校验 func/cmd，任何帧都按"N×4B float"解析；payload 对齐不校验 |
| `dataservice.cpp` 析构 | P1 | Worker 以 parent=nullptr 创建后从不 deleteLater，析构仅 quit+wait，泄漏 |
| `dataservice.cpp startAcquisition` | P1 | 启停只是置标志+发信号，不真正控制数据流；停止采集=断开设备，语义混淆 |
| `mainwindow.cpp connectDevice` | P1 | IP/端口写死 127.0.0.1:40001 且启动即自动连接，不读 ConfigManager |
| `monitorpage.*` | P1 | 通道名/单位/量程/报警阈值全部 UI 硬编码；报警判定放 View 层 |
| `signalslotdemo`+`DemoPage` | P2 | 教学对象编译进正式 exe 并占一个页签 |
| 全工程注释 | P2 | 教学注释堆砌侵入生产代码，未分层 |
| `dataservice.cpp` | P2 | new QMutex 手动堆管理；锁纪律不一致；实际无真实竞态（过度设计） |
| `recordmanager.cpp appendData` | P2 | 每次批量新建 QTextStream，依赖析构 flush；写失败静默吞掉 |
| `acquisitionworker.cpp`+`simulator/main.cpp` | P2 | memcpy 位模式解释 float，隐含 sizeof==4 假设 |
| `dataservice.cpp lastData()` | P3 | 快照 API 死代码 |
| `devicecontroller.cpp` | P3 | 重连边界粗糙：Connecting 态重复 connectDevice 重复投递 start |
| `linechartwidget.cpp appendPoint` | P3 | QVector::removeFirst() O(n)，500 点高频缓冲整体移位 |
| `byteutils.cpp fromHexString` | P3 | 手工状态机晦涩，"1xAA" 误解析 |
| `tcpclient.h/cpp` | P3 | ProtocolFrame 未注册元类型（同线程直连侥幸可用） |
| `dataservice.cpp` 注释 | P3 | 注释称"必须 main() 注册"但实际在构造函数内，注释与实现不符 |
| `signalslotdemo.cpp demoDeleteLater` | P3 | 名为 deleteLater 演示，实际未调用 deleteLater |
| 测试体系 | P3 | UI 层零测试；DataService 析构/线程销毁泄漏路径无测试 |
| `CMakeLists.txt` | P3 | 教学目标渗透模块划分（tst_signalslotdemo 直接编教学代码） |

### 评分
- **架构合理性：4.0/10** —— 分层骨架与依赖方向干净，但血肉缺失。
- **C++代码质量：7.0/10** —— 现代 C++ 纪律良好，但存在毛刺与教学注释过载。
- **Qt使用正确性：6.0/10** —— 关键范式对，但 worker 泄漏、无谓互斥锁、字符串式 invokeMethod、元类型缺注册。
- **Model/View设计：1.0/10** —— 完全缺失。

---

## 四、审查维度 3：多线程 / 通信 / 错误处理（专项 Agent）

**总体结论**：协议解析层质量较高（半包/粘包/重同步/LEN 防御/CRC 均有正确实现且有测试），但服务层有 1 个关键架构问题——**DeviceController 状态机与自动重连完全未接入真实应用**，断线后无任何自动恢复能力。另有 Worker 泄漏、disconnected 信号双发引发误重连、connectTo 无锁读标志等线程/生命周期缺陷。

### 问题清单（要点）

| 位置 | 严重度 | 问题 |
|------|--------|------|
| `dataservice.cpp:51,65-74` | P1 | **Worker 对象泄漏**：析构只 quit+wait(3000) 从不删除 worker |
| `dataservice.cpp:122-135`+`devicecontroller.cpp:107-118` | P1 | **disconnected 信号双发**：stopAcquisition 同步 emit + worker 再发一次 → 用户断开后误判"意外断线" → 1s 后自动重连 |
| `devicecontroller.h/cpp`+`mainwindow.cpp:58-86` | P1 | **状态机/退避重连未接入应用**：MainWindow 直接用 DataService，正式程序断线后无自动重连，违背契约 P13 |
| `dataservice.cpp:85` | P2 | connectTo 无锁读 m_connected（数据竞争）；Connecting 期间二次点击被静默吞掉（"失败但假装成功"） |
| `acquisitionworker.cpp:88-131` | P2 | handleFrame 不过滤 func/cmd；payload.size()%4 非 0 静默丢尾 |
| `dataservice.cpp:65-74` | P2 | 关停序列脆弱：wait(3000) 超时后 QThread 被销毁仍运行 → "Destroyed while thread is still running" 崩溃 |
| `dataservice.cpp:105-135` | P2 | m_acquiring 仅主线程标志，连接失败/断线后不复位，UI 显示"采集中"实无 socket |
| `devicecontroller.cpp:162` | P2 | 退避 1<<m_retryAttempts 在 int 上无限左移（UB），qMin 封顶挡不住溢出 |
| `tcpclient.cpp:68-82` | P2 | sendFrame 无连接超时：不可达 IP 可长期停在 Connecting |
| `tcpclient.cpp:80-81` | P3 | sendFrame 部分写处理缺失；write 失败无错误上报 |
| `tcpclient.cpp` | P2 | sendFrame 非线程安全且是"死接口"，无人负责补发送 |
| `dataservice.h/cpp` | P2 | 跨线程连接未显式指定类型，依赖 auto 推断 |
| `recordmanager.cpp:88-133` | P2 | loadReplay "失败但假装成功"：0 帧仍返回 true |
| `recordmanager.cpp:105-118` | P3 | CSV 容错过度：非数字按 0 处理，畸形行聚合到时间戳 0 产生假数据 |
| `dataservice.cpp:159-172` | P2 | 错误类型不可区分（全 QString）；CRC 校验失败/解析异常被静默丢弃 |
| `acquisitionworker.cpp:33-40` | P3 | Worker 析构兜底是死代码（deleteLater 在已退出线程永不执行） |
| `dataservice.cpp:105-120` | P3 | startAcquisition 与 worker 无动作关联，与 connectTo 顺序耦合 |
| `simulator/main.cpp:172-176` | P3 | 定时器遍历 clients 时 write 可能触发 removeAll 迭代失效 |

### 评分
- **多线程设计：4.0/10** —— 思路对、边界糙。
- **通信设计：5.0/10** —— 协议解析层健壮，但无连接超时、sendFrame 缺陷、协议错误零上报、重连未接入。
- **错误处理：3.5/10** —— 错误链完整统一转 QString 是亮点，但"失败但假装成功"类问题普遍。

### 最高优先级修复建议
1. 接入 DeviceController（让真实应用拥有状态机与退避重连）
2. 修复 disconnected 双发（统一连接状态信号来源）
3. 修复 Worker 泄漏（finished→deleteLater 标准收尾）
4. 修复 connectTo 数据竞争与二次请求吞没
5. 补连接超时（否则重连在不可达目标上形同虚设）
6. handleFrame 加 func/cmd 白名单过滤

---

## 五、审查维度 4：测试 / 文档 / 工程真实度（专项 Agent）

**总体结论**：12 套件 121 断言**真实运行全绿**（非"仅编译通过"），协议层（半包/粘包/CRC 篡改/LEN 越界/重同步）测试质量高。但 Integration/Simulator/Smoke/Deployment/Error 分层体系为零（`tests/integration`、`tests/smoke` 是空目录），无 CI，无压力/模糊测试，领域模型无行为无法测不变量，模拟设备零异常注入。

### 测试体系 9 层核对

| 分类 | 状态 | 现有内容 | 缺失项 |
|------|------|----------|--------|
| Unit | △ | ByteUtils/TimeUtils/Config/Log/Domain/Widgets 单测齐全 | 领域不变量测试（领域无行为无从测起）；报警/状态转移单测 |
| Integration | ✗ | `tests/integration/` 空目录 | 设备→通信→协议→数据→Model→UI 端到端；记录→回放闭环 |
| Protocol | ✓（最强） | 组帧/解析/半包/粘包/CRC/SOF 重同步/LEN 越界/标准向量 | 随机模糊测试；非法 FUNC/CMD；超大载荷性能；坏帧风暴 |
| Communication | △ | 回环收发/半包/粘包/连接失败/断线重连 | 超时测试；半开连接；服务端主动断开；重连风暴上限 |
| Thread | △ | 跨线程投递、启停、断开 | Worker 重复启停；线程退出时序竞态；高帧率队列堆积 |
| Error | ✗ | 仅连不上端口/CRC 错/LEN 越界 | 错误注入后恢复；设备忙/报警触发/恢复；重连退避上限 |
| Simulator | ✗ | 无针对 simulator 的测试（各测试内建伪设备绕开它） | simulator 自身测试；异常注入矩阵；正常/异常/压力模式 |
| Smoke | ✗ | `tests/smoke/` 空目录；仅文档手工步骤 | 进程级自动化冒烟脚本 S1~S5 |
| Deployment | ✗ | deploy/ 为手工 windeployqt 产物 | 发布包自动化验证（dll 完整性、独立环境运行） |

### 问题清单（要点）

| 位置 | 严重度 | 问题 |
|------|--------|------|
| `tests/integration/`、`tests/smoke/` | P0 | 两个目录为空，所谓"集成/冒烟测试"只在文档金字塔图里 |
| `src/simulator/main.cpp` | P0 | 模拟设备只会每 50ms 发 4 路正弦波，无断线/延迟/粘包/CRC/非法帧/设备忙/报警/压力注入能力 |
| `monitorpage.cpp:206-212` | P0 | 报警业务逻辑写在 View 层（value>m_max*0.85 内联），领域无 AlarmRule/AlarmEvent |
| `domain/models.h/cpp` | P1 | 纯 getter/setter + struct，无任何不变量/校验/行为 |
| `domain/models.h`+`simulator/main.cpp`+`monitorpage.cpp` | P1 | Device/Channel 模型与真实数据链路脱节（死模型），Device 从未参与运行期数据流 |
| `settingspage.cpp`、`devicepage.cpp` | P1 | 纯占位 Label；ConfigManager 真实可用但应用零消费 |
| 全工程 | P1 | 无 CI/无一键全量验证；本机 ctest 不在 PATH |
| `docs/08-项目总结.md` | P1 | 文档夸大："可交付的工业级软件"、"正确性★5" 与 V2 自认"Demo 集合"直接矛盾 |
| `docs/stages/P01~P15.md` | P1 | 15 份阶段文档全部无练习/思考题（grep 0 命中） |
| `tst_dataservice.cpp:192` | P1 | stopAcquisition_stopsData 用 qWait(300) 固定等待，与文档宣称"绝不裸 sleep"矛盾 |
| `dataservice.cpp:122-135` | P1 | stopAcquisition 语义化为断开连接，UI 状态栏误报"设备已断开" |
| `recordmanager.cpp:69-83` | P1 | 记录在主线程 dataUpdated 槽内同步写 CSV，高频下有丢帧/阻塞 UI 风险 |
| `tst_signalslotdemo.cpp:70-80` | P2 | 只断言 spy.count()>=5 不验证内容；clock 空等 1.2s |
| `docs/06-测试文档.md:3` | P2 | 头部写"11/11"正文写 12/12，P15 后未同步 |
| `tst_tcpclient.cpp:112-142` | P2 | 绑定-释放法找空闲端口，实测 4.3s 且可能 flaky |
| `tcpclient.cpp` | P2 | 无心跳/keep-alive/超时，半开连接（网线拔断）无法感知 |
| `acquisitionworker.cpp:88-98` | P2 | handleFrame 不校验 payload 对齐、非法 func/cmd |
| `dataservice.cpp:76-91` | P2 | 连接状态缺防重入保护，存在竞态窗口 |
| `protocoltypes.h` | P2 | 帧结构无序列号/时间戳字段，回放与实时数据无法区分来源 |
| `devicecontroller.cpp:162` | P2 | 重连退避 int 无限左移 UB |
| `docs/00-项目规程报告.md:318-324` | P3 | 文档编号表与实际 docs 目录陈旧不一致 |
| `src/services/` | P3 | 错误全 QString 无错误码/分类，UI 无法按类别降级 |
| `deploy/` | P3 | 手工 windeployqt 无自动化校验；simulator 1.0.0 与主程序 0.1.0 版本不一致 |
| `tst_protocol.cpp` | P3 | 无随机模糊测试、无性能基准 |
| `monitorpage.cpp:94-96` | P3 | 通道名/单位硬编码在视图层，与领域 Channel 重复 |

### 评分
- **测试完整度：6.0/10** —— 真实运行全绿但分层体系缺失。
- **文档质量：7.0/10** —— 体系完整、诚实度分裂（使用说明诚实、总结夸大）。
- **工程真实度：5.0/10** —— "真实骨架 + Demo 填充"的中间态。

---

## 六、主 Agent 独立核对结论

- **Model/View 完全缺失**：全工程 grep `QAbstractTableModel/QStandardItemModel/QTableView/QListView/setModel` 零结果，P8 名为 Model/View 实际只是 struct + 直接显示。
- **领域模型不完整**：domain/models.h 仅 4 类型，缺 AlarmRule/AlarmEvent/DeviceState/AcquisitionRecord，Device 无通信参数/通道配置建模。
- **设置页/设备页占位确认**：settingspage.cpp 仅标题+占位文字，devicepage.cpp 仅说明文字。
- **V1 综合定位**：分层与协议真实的 Demo 集合。值得肯定：协议引擎、单测真实运行、文档体系、深色主题、多线程基本范式。必须重构：领域模型补行为、Model/View 补实现、模拟器补注入、UI 去占位、错误处理去"假装成功"、DeviceController 接入、测试补分层、教学注释外迁。

---

*本报告为 V1 严格审查结论，V2 执行依据。整改路线见 `docs/11-DataScope Studio V2 工程质量提升计划.md`。*
