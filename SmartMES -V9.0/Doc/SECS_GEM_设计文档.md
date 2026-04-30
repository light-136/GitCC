# SmartMES V9.0 SECS/GEM 半导体通信设计文档

## 1. 概述

本文档描述 SmartMES V9.0 中 SECS/GEM 半导体通信模块的设计方案。该模块遵循 SEMI（国际半导体设备与材料协会）制定的三个核心标准：

| 标准 | 名称 | 内容 |
|------|------|------|
| SEMI E5 | SECS-II | 消息内容格式（数据编码） |
| SEMI E30 | GEM | 通用设备模型（行为规范） |
| SEMI E37 | HSMS | 高速消息服务（传输协议） |

### 1.1 设计目标
- 实现完整的 HSMS 传输层（TCP 连接、状态机、定时器）
- 实现 SECS-II 二进制编解码（递归 SecsItem 树）
- 实现 GEM 合规层（状态机、数据管理、告警、远程命令、工艺程序）
- 提供模拟 GEM 主机用于测试和学习
- 全仿真模式可运行

---

## 2. HSMS 传输层（SEMI E37）

### 2.1 HSMS 消息帧格式
```
┌──────────────────────────────────────────────┐
│ Length Bytes (4)  │  Message Header (10)      │
│ (大端序)          │                           │
├──────────────────┼───────────────────────────┤
│ 总长度(不含自身)  │ SessionID (2 bytes)       │
│                  │ HeaderByte2 (1 byte)       │
│                  │   bit7: W-bit              │
│                  │   bit6-0: Stream           │
│                  │ HeaderByte3 (1 byte)       │
│                  │   Function                 │
│                  │ PType (1 byte) = 0x00      │
│                  │ SType (1 byte)             │
│                  │ SystemBytes (4 bytes)      │
├──────────────────┴───────────────────────────┤
│ Message Body (可变长度)                       │
│ SECS-II 编码数据                              │
└──────────────────────────────────────────────┘
```

### 2.2 HSMS 消息类型（SType）
| SType | 名称 | 方向 | 说明 |
|-------|------|------|------|
| 0 | Data Message | 双向 | SECS-II 数据消息 |
| 1 | Select.req | 主动方→被动方 | 请求建立会话 |
| 2 | Select.rsp | 被动方→主动方 | 会话建立响应 |
| 3 | Deselect.req | 任一方 | 请求断开会话 |
| 4 | Deselect.rsp | 对方 | 断开会话响应 |
| 5 | Linktest.req | 任一方 | 心跳请求 |
| 6 | Linktest.rsp | 对方 | 心跳响应 |
| 7 | Reject.req | 任一方 | 拒绝消息 |
| 9 | Separate.req | 任一方 | 立即断开（无需响应） |

### 2.3 HSMS 状态机
```
                    TCP Connect
    NotConnected ──────────────→ NotSelected
         ▲                           │
         │                     Select.req/rsp
         │                           │
         │                           ▼
         │                       Selected
         │                           │
         ├── Separate.req ───────────┘
         ├── TCP Disconnect ─────────┘
         └── T7 Timeout ────────────┘
```

### 2.4 HSMS 定时器
| 定时器 | 默认值 | 触发条件 | 超时动作 |
|--------|--------|----------|----------|
| T3 | 45秒 | 发送数据消息后 | 断开连接 |
| T5 | 10秒 | 连接断开后 | 允许重连 |
| T6 | 5秒 | 发送控制消息后 | 断开连接 |
| T7 | 10秒 | 进入 NotSelected 后 | 断开连接 |
| T8 | 5秒 | 接收消息字节间 | 断开连接 |

---

## 3. SECS-II 消息格式（SEMI E5）

### 3.1 数据项编码
```
┌─────────────────────────────────┐
│ Format Byte (1 byte)            │
│   bit7-2: Format Code           │
│   bit1-0: Number of Length Bytes│
├─────────────────────────────────┤
│ Length Bytes (1-3 bytes)        │
│   大端序，表示数据字节数         │
│   List类型表示子项数量           │
├─────────────────────────────────┤
│ Data (可变长度)                  │
│   数值类型：大端字节序           │
│   ASCII：UTF-8 编码             │
│   List：递归编码子项             │
└─────────────────────────────────┘
```

### 3.2 数据类型
| 格式码 | 类型 | 字节数 | C# 类型 |
|--------|------|--------|---------|
| 0x00 | List | — | List\<SecsItem\> |
| 0x20 | Binary | 1/item | byte[] |
| 0x24 | Boolean | 1 | bool |
| 0x40 | ASCII | 1/char | string |
| 0x60 | I8 | 8 | long |
| 0x64 | I1 | 1 | sbyte |
| 0x68 | I2 | 2 | short |
| 0x70 | I4 | 4 | int |
| 0x80 | F8 | 8 | double |
| 0x90 | F4 | 4 | float |
| 0xA0 | U8 | 8 | ulong |
| 0xA4 | U1 | 1 | byte |
| 0xA8 | U2 | 2 | ushort |
| 0xB0 | U4 | 4 | uint |

### 3.3 SML 文本表示
SECS Message Language (SML) 是 SECS-II 消息的文本表示格式：
```
S1F13 W
<L [3]
  <A "SmartMES">
  <A "V9.0">
  <U4 1>
>
```

### 3.4 编码示例
S1F13 消息体编码：
```
01 03        ; List, 3 items, 1 length byte
41 08        ; ASCII, 8 bytes
53 6D 61 72 74 4D 45 53  ; "SmartMES"
41 04        ; ASCII, 4 bytes
56 39 2E 30  ; "V9.0"
B1 04        ; U4, 4 bytes
00 00 00 01  ; value = 1
```

---

## 4. GEM 合规层（SEMI E30）

### 4.1 通信状态模型
```
                Enable()
    Disabled ──────────→ WaitCommunicating
       ▲                       │
       │                 S1F13/S1F14
       │                 成功
       │                       │
       │                       ▼
       │                 Communicating
       │                       │
       │                  通信失败
       │                       │
       │                       ▼
       └── Disable() ── NotCommunicating
```

### 4.2 控制状态模型
```
                    请求上线
    EquipmentOffline ──────→ AttemptOnline
           ▲                      │
           │              ┌───────┴───────┐
           │              │               │
           │         主机接受          主机拒绝
           │              │               │
           │              ▼               ▼
           │        OnlineRemote    HostOffline
           │              │
           │         操作员切换
           │              │
           │              ▼
           │        OnlineLocal
           │              │
           └── 请求离线 ──┘
```

### 4.3 核心消息集

#### 通信管理
| 消息 | 方向 | 功能 |
|------|------|------|
| S1F1/S1F2 | 双向 | 心跳（Are You There / I Am Here） |
| S1F13/S1F14 | E→H | 建立通信请求/响应 |
| S1F15/S1F16 | H→E | 请求离线/响应 |
| S1F17/S1F18 | E→H | 请求上线/响应 |

#### 状态变量查询
| 消息 | 方向 | 功能 |
|------|------|------|
| S1F3/S1F4 | H→E | 查询指定 SV 值 |
| S1F11/S1F12 | H→E | 查询 SV 名称列表 |

#### 设备常量管理
| 消息 | 方向 | 功能 |
|------|------|------|
| S2F13/S2F14 | H→E | 查询 EC 值 |
| S2F15/S2F16 | H→E | 设置 EC 值 |
| S2F29/S2F30 | H→E | 查询 EC 名称列表 |

#### 报告与事件
| 消息 | 方向 | 功能 |
|------|------|------|
| S2F33/S2F34 | H→E | 定义报告 |
| S2F35/S2F36 | H→E | 关联事件与报告 |
| S2F37/S2F38 | H→E | 启用/禁用事件上报 |
| S6F11/S6F12 | E→H | 事件上报/确认 |

#### 告警管理
| 消息 | 方向 | 功能 |
|------|------|------|
| S5F1/S5F2 | E→H | 告警设置/清除 |
| S5F5/S5F6 | H→E | 列出告警 |
| S5F7/S5F8 | H→E | 列出已启用告警 |

#### 远程命令
| 消息 | 方向 | 功能 |
|------|------|------|
| S2F41/S2F42 | H→E | 远程命令/响应 |

#### 工艺程序管理
| 消息 | 方向 | 功能 |
|------|------|------|
| S7F1/S7F2 | H→E | PP 加载询问 |
| S7F3/S7F4 | H→E | PP 发送 |
| S7F5/S7F6 | E→H | PP 请求 |
| S7F17/S7F18 | H→E | PP 删除 |
| S7F19/S7F20 | H→E | PP 目录列表 |

### 4.4 HCACK 返回码（远程命令）
| 值 | 含义 |
|----|------|
| 0 | 命令执行成功 |
| 1 | 无效命令 |
| 2 | 当前无法执行 |
| 3 | 参数错误 |
| 4 | 已确认但未开始 |
| 5 | 已在目标状态 |
| 6 | 对象不存在 |

---

## 5. 模块架构

### 5.1 文件清单
| 文件 | 类 | 职责 |
|------|-----|------|
| HsmsMessage.cs | HsmsHeader, HsmsFrame | HSMS 消息帧编解码 |
| SecsIICodec.cs | SecsIICodec | SECS-II 二进制编解码 |
| HsmsConnection.cs | HsmsConnection | HSMS TCP 传输层 |
| GemStateMachine.cs | GemStateMachine | GEM 双层状态机 |
| GemDataManager.cs | GemDataManager | SV/EC/CE/报告管理 |
| GemAlarmManager.cs | GemAlarmManager | 告警管理 |
| GemRemoteCommandHandler.cs | GemRemoteCommandHandler | 远程命令处理 |
| GemProcessProgramManager.cs | GemProcessProgramManager | 工艺程序管理 |
| SecsGemService.cs | SecsGemService | 顶层编排服务 |
| SecsGemHostSimulator.cs | SecsGemHostSimulator | 模拟 GEM 主机 |

### 5.2 模块依赖
```
SecsGemService（顶层编排）
    ├── HsmsConnection（传输层）
    │       └── HsmsMessage（消息帧）
    │       └── SecsIICodec（编解码）
    ├── GemStateMachine（状态机）
    ├── GemDataManager（数据管理）
    ├── GemAlarmManager（告警）
    ├── GemRemoteCommandHandler（远程命令）
    └── GemProcessProgramManager（工艺程序）
```

---

## 6. 典型通信流程

### 6.1 设备上线流程
```
1. TCP 连接到主机
2. 发送 Select.req，等待 Select.rsp
3. 发送 S1F13（建立通信），等待 S1F14
4. 发送 S1F17（请求上线），等待 S1F18
5. 进入 OnlineRemote 状态
6. 启动心跳定时器（每10秒 S1F1/S1F2）
```

### 6.2 事件上报流程
```
1. 设备检测到事件（如加工完成）
2. 查找事件关联的报告定义
3. 收集报告中引用的变量值
4. 构建 S6F11 消息体
5. 发送 S6F11，等待 S6F12 确认
```

### 6.3 远程命令处理流程
```
1. 收到 S2F41 消息
2. 解析 RCMD（命令名称）
3. 解析 CPNAME/CPVAL（命令参数）
4. 查找注册的命令处理器
5. 执行命令处理器
6. 构建 S2F42 回复（HCACK + 参数确认）
7. 发送 S2F42
```
