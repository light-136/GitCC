# 05-TCP链路与模拟设备契约

> 适用范围：主程序 DataScope 的 TCP 客户端（`TcpClient`）与采集设备（真实设备或 `Simulator` 模拟设备）之间的链路约定。

## 1. 连接约定

| 角色 | 地址 | 端口 |
| --- | --- | --- |
| 客户端（DataScope） | 127.0.0.1（默认） | 40001（默认） |
| 服务端（Simulator） | 0.0.0.0（所有网卡） | 40001（可用 `--port` / `-p` 覆盖，范围 1-65535） |

Simulator 通过 `QTcpServer::listen(QHostAddress::Any, port)` 监听；启动失败打印"监听失败"并退出。

## 2. TcpClient 接口契约

公开方法：

| 方法 | 说明 |
| --- | --- |
| `connectToHost(host, port)` | 异步发起连接，结果由信号通知 |
| `disconnectFromHost()` | 主动断开 |
| `isConnected()` | 仅 `ConnectedState` 视为已连接 |
| `sendFrame(frame)` | 用 `FrameBuilder` 组帧写出；未连接或组帧失败（载荷超限）返回 false |

信号：

| 信号 | 触发条件 |
| --- | --- |
| `connected()` | socket 连接成功 |
| `disconnected()` | 主动断开 / 对端关闭 / 网络异常 |
| `errorOccurred(QString)` | socket 错误，统一转 `errorString()` 字符串，业务层不感知错误枚举 |
| `frameReceived(ProtocolFrame)` | 收到一帧完整且 CRC 校验通过的帧 |

错误处理链路：`QTcpSocket` 的 `error` 信号 → `onSocketError` → 统一转 QString 发出 `errorOccurred`。数据到达链路：`readyRead` → `readAll()` → `FrameParser::feed` → 循环 `nextFrame` → 逐帧 `frameReceived`。

## 3. 发送帧格式要求

完全复用契约 03：`sendFrame` 内部调用 `FrameBuilder::build(func, cmd, payload)`，写出 `SOF+FUNC+CMD+LEN(大端)+DATA+CRC16(低字节在前)`。未连接时拒绝发送，避免向未打开设备写入。

## 4. Simulator 行为契约

1. **多客户端**：每客户端独立 `FrameParser`，半包/粘包状态按连接隔离；断开时移除并 `deleteLater`。
2. **响应帧**：收到任意合法帧（CRC 通过）即记录日志，并以 `FUNC|0x80`（同 CMD、同载荷）组帧回发，验证双向链路。
3. **模拟采集数据**：每 50ms 向所有连接态客户端发一帧：FUNC=0x01，CMD=0x01，载荷 = 4 通道 × 4 字节 float（大端）。
4. **正弦波形**：各通道幅度/角频率/初相位不同（如 10/8/5/3，1/2/3/0.5Hz，0/π/2/π/π/4），相位每周期递增 0.05，便于 UI 区分。
5. 只向 `ConnectedState` 客户端写帧，防止写已断开 socket 误报。

## 5. 断线语义与重连前提

- 断开由 `disconnected` 信号向上传递；`TcpClient` 自身不重连。
- **重连前提**：`AcquisitionWorker` 在收到 `errorOccurred` 时立即 `deleteLater()` 并置空 `m_client`，使后续 `start()` 能重建一个全新连接；重连是在全新客户端上发起，而非在残留坏 socket 上重试。这是 `DeviceController` 自动重连能工作的关键。

---

## 6. 模拟设备运行模式契约（V2-执行③）

> 设备行为由 `SimulatorDevice::setMode(SimulatorMode)` 控制；独立程序与集成测试共用同一份逻辑。

| 模式 | 枚举值 | 行为契约 |
| --- | --- | --- |
| 正常 | `Normal` | 4 通道正弦波（幅度 10/8/5/3，角频率 1/2/3/0.5Hz，初相位 0/π/2/π/π/4），相位每帧递增 0.05；帧间隔默认 50ms（20Hz） |
| 报警 | `Alarm` | 每 20 帧翻转一次"报警/恢复"相位；报警相位把通道 0 推到 200.0（超出 0..100 量程，触发主机报警与恢复链路） |
| 压力 | `Stress` | 帧间隔强制 10ms + 每帧 Sticky 粘包（两帧合并一次发送），考验主机吞吐与半包重组；`setMode` 内部一次性完成两项配置 |
| 设备忙 | `Busy` | 每 50 帧一个忙周期，周期末尾连续 20 帧静默（不输出数据帧，连接保持）——模拟设备忙/超时 |
| 错误 | `Error` | 按 `FaultConfig` 注入指定异常（见第 7 节） |

通用行为（所有模式）：
- 载荷 = `通道数 × 4` 字节 float 大端，FUNC=0x01、CMD=0x01；
- 通道数 `setChannelCount(n)` 可改（默认 4，少于 1 时钳制为 1）；超过 4 通道时名称/单位/量程按模板循环使用；
- 只向 `ConnectedState` 客户端写帧，防止写已断开 socket 误报；
- 通道配置查询应答（0x83）在所有模式下都可用（见第 8 节）。

## 7. 异常注入契约（FaultInjector）

> 注入器是**纯逻辑类**（无 TCP 依赖）：输入一帧正常帧字节，输出 0..N 段字节流（每段对应一次 `write` 调用）。`SimulatorDevice` 在 Error 模式调用它，集成测试直接调用它——同一份注入逻辑两处复用。

`FaultConfig` 字段：`type`（异常类型）、`period`（触发周期：每 period 帧注入一次，≤0 视为每帧）、`channelIndex`（Surge 作用的通道）、`surgeValue`（突变值，默认 200.0）。

| 异常类型 | 注入动作 | 输出 | 主机应对（测试断言点） |
| --- | --- | --- | --- |
| `None` | 无 | 原样一帧 | 正常收帧 |
| `Sticky` 粘包 | 两帧拼接为一段 | 1 段 = 2 帧 | 半包/粘包重组，逐帧产出 |
| `Fragment` 分包 | 一帧从中间拆两段 | 2 段 | 累积缓冲拼接出完整帧 |
| `CrcError` | 帧尾最后 1 字节 +1（每帧） | 篡改帧 | CRC 校验拒绝，坏帧不产出 |
| `IllegalFrame` | LEN 字段（下标 4-5）改为 0xFFFF（每帧） | 篡改帧 | 解析器判定非法帧头，防御性拒绝、不撑爆缓冲 |
| `Delay` 延迟 | 到达触发周期的那一帧**不发送** | 空（本次不发） | 主机超时处理 / 数据中断感知 |
| `Surge` 突变 | 把指定通道的 4 字节 float 替换为 `surgeValue`，并**重算 CRC**（帧保持合法） | 篡改帧 | 主机正常收帧并触发超量程报警（突变≠通信故障） |
| `Disconnect` 断线 | 返回空（不发送）；真正的断开由 `SimulatorDevice` 在设备层对全部连接客户端执行 `disconnectFromHost` | 空 | 主机感知断线并自动重连 |

触发规则与 `period`：`shouldTrigger(frameSeq)` = `period<=0 || frameSeq % period == 0`。**命令行默认 period=1**，即 Delay 每帧都不发、Surge 每帧都突变、Disconnect 每帧都断——如需周期性注入须在测试/代码里调大 period。

## 8. 通道配置查询（设备侧应答，V2-执行③）

设备接收主机发来的查询请求帧，应答配置帧（完整帧格式见契约 03 第 8 节）：

1. 每客户端独立 `FrameParser` 收集请求字节（不同客户端互不污染）；
2. 收到 `FUNC=0x03 && CMD=0x01` 查询帧 → 用当前 `m_channelConfig`（名称/单位/量程）编码载荷，以 `FUNC=0x83`、`CMD=0x01` 应答；
3. 其它请求暂不支持：**静默忽略**（不崩溃、不回错误，保持协议向前兼容）。

通道配置与采集帧联动：`setChannelCount(n)` 重建配置 → 采集帧通道数、配置帧通道数同步变化；主机据此动态增删卡片。

## 9. 命令行契约（DataScopeSimulator，V2）

独立程序 `simulator.exe`，用 `QCommandLineParser` 解析：

| 参数 | 简写 | 取值 | 默认 | 说明 |
| --- | --- | --- | --- | --- |
| `--port` | `-p` | 1-65535 | 40001 | 监听端口；非法（含 0）→ 报错退出码 1 |
| `--mode` | `-m` | normal/alarm/stress/busy/error | normal | 运行模式；未知值回退 normal |
| `--fault` | `-f` | none/crc/sticky/fragment/illegal/delay/surge/disconnect | none | 异常类型；**仅 `--mode error` 生效**；未知值回退 none |
| `--frame-interval` | `-i` | 正数（毫秒） | 50 | 数据帧间隔；非法（≤0）→ 报错退出码 1 |
| `--help` / `--version` | | | | QCommandLineParser 内置 |

启动约定：
- 监听 `0.0.0.0`（所有网卡）；端口被占用 → 打印"监听失败"退出码 1；
- 启动成功打印一行摘要：`模拟设备已启动（V2）：0.0.0.0:<port>  模式=<mode>  帧间隔=<interval>ms`；
- `Error` 模式打印 `异常注入已配置:<fault>` 提示当前注入类型。

教学对照：命令行可配置化把模拟器从"写死的演示程序"升级为**可复用的测试基础设施**（对应 WPF 侧手写参数解析 / System.CommandLine）。
