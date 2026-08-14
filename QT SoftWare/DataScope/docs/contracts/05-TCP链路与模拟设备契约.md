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
