# DataScope Studio · TCP 链路与模拟设备契约

> 文档编号：02-05　版本：v1.0　日期：2026-08-14
> 用途：P11 阶段 TcpClient 与 Simulator（模拟设备）的接口契约

---

## 一、总体拓扑

```
┌─ 主程序 DataScope ──┐                ┌─ 模拟设备 Simulator(独立进程) ─┐
│ TcpClient(客户端)     │  TCP/IP        │ QTcpServer(服务端)             │
│  QTcpSocket          │◄──────────────►│  监听 0.0.0.0:40001           │
│  FrameParser 解析帧   │  帧格式字节流    │  定时组帧发送（正弦波形）        │
└──────────────────────┘                └───────────────────────────────┘
```

## 二、网络约定

- 协议：TCP（可靠字节流，粘包/半包由帧解析器处理）。
- 默认端口：**40001**（后续可用 ConfigManager 配置，键 `net/port`）。
- 数据：客户端/服务端双向都走 [帧格式契约](./03-通信协议帧格式契约.md)。

## 三、Simulator（模拟设备，独立可执行程序）

```
target: simulator           源文件: src/simulator/main.cpp
```

行为：
1. 启动监听 `0.0.0.0:40001`；状态栏/控制台输出监听地址。
2. 收到客户端连接 → 记录并输出日志。
3. 收到客户端**任意帧** → 用 FrameParser 解析，构造响应帧（`func | 0x80`）回发，验证链路双向。
4. 每 **50ms** 定时向所有已连接客户端发送一帧"模拟采集数据"：
   - CMD=0x01 采集数据；载荷 = 4 通道 × 4 字节 float（大端），值 = 各通道正弦波（幅度/相位不同）。
5. 断开连接 → 移除客户端并输出日志。

可选：用 `QCommandLineParser` 支持 `--port` 覆盖端口。

## 四、TcpClient（主程序侧）

```cpp
namespace datascope::services {
class TcpClient : public QObject {
    Q_OBJECT
public:
    explicit TcpClient(QObject *parent = nullptr);
    void connectToHost(const QString &host, quint16 port);
    void disconnectFromHost();
    bool isConnected() const;
    bool sendFrame(const protocol::ProtocolFrame &frame);   // 组帧并写出
signals:
    void connected();                            // 连接成功
    void disconnected();                         // 断开（主动或被动）
    void errorOccurred(const QString &message);  // 错误（QAbstractSocket::error）
    void frameReceived(const protocol::ProtocolFrame &frame);  // 解析出的完整帧
private:
    QTcpSocket *m_socket = nullptr;
    protocol::FrameParser m_parser;              // 帧解析器（C1 交付）
};
}
```

要点：
- readyRead → 读全部字节 → `m_parser.feed(chunk)` → 循环 `nextFrame()` → emit frameReceived。
- sendFrame 用 FrameBuilder::build 组帧后 write。
- 错误统一转成 QString 信号（教学：错误信息不上抛裸 socket code）。

## 五、测试强制项（tst_tcpclient.cpp）

1. **本地回环**：测试内建 QTcpServer（QTcpServer 监听 127.0.0.1:0 随机端口）作为"伪设备"，TcpClient 连接它；
2. 伪设备用 FrameBuilder 发一帧 → TcpClient 的 frameReceived 信号收到该帧（QSignalSpy + QTest::qWait 事件循环等待）；
3. 连不存在的端口 → errorOccurred 信号触发；
4. 半包：伪设备分两段发一帧 → 仍能完整解析。

> Simulator 为独立程序，不强制单测；主 Agent 集成时以"连接回环冒烟"验证。
