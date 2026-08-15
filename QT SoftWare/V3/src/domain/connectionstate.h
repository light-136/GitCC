/**
 * @file connectionstate.h
 * @brief V3 领域层 —— 连接状态机与重连退避策略（头文件）
 *
 * ── 开发思路 ──
 * 设备连接的生命周期不能乱跳（例如"未连接"不能直接变"已连接"、"已连接"不能
 * 直接变"连接中"）。把"合法转移表"集中到 canTransition() 一处，任何想推进
 * 状态的调用方都必须先问它 —— 状态机不变量内聚在领域层。
 *
 * 同时把"失败重连的等待时间"也收进领域层：nextRetryDelayMs() 做指数退避，
 * 避免设备故障时采集端疯狂重连打满 CPU/网络，这是健壮网络客户端的标准做法。
 *
 * ── WPF 对照 ──
 *   enum class ConnectionState  ↔  C# 强类型枚举（连接状态机）
 *   canTransition()             ↔  C# 中状态机的"合法转移表"（switch + 白名单）
 *   connectionStateName()       ↔  C# 枚举 ToString() + 资源字典本地化
 *   nextRetryDelayMs()          ↔  C# 重试库（如 Polly）里的指数退避策略
 */

#pragma once

#include <QString>

namespace dscope {
namespace domain {

/**
 * @enum ConnectionState
 * @brief 设备连接状态
 */
enum class ConnectionState {
    Disconnected,   ///< 未连接（初始状态）
    Connecting,     ///< 连接中（异步进行中）
    Connected,      ///< 已连接
    Error           ///< 错误（连接失败/异常断开，可重连）
};

/**
 * @brief 判断状态转移是否合法（合法转移表）
 * @param from 起始状态
 * @param to   目标状态
 * @return true 允许转移
 *
 * 合法转移：
 *   Disconnected → Connecting                 （发起连接）
 *   Connecting   → Connected / Error          （连接成功 / 连接失败）
 *   Connected    → Error / Disconnected       （意外断线 / 用户主动断开）
 *   Error        → Connecting / Disconnected  （重试连接 / 放弃并断开）
 */
bool canTransition(ConnectionState from, ConnectionState to);

/**
 * @brief 状态的中文显示名
 * @return "未连接"/"连接中"/"已连接"/"错误"；未识别返回"未知"
 * @note 纯函数，实现见 connectionstate.cpp。
 */
QString connectionStateName(ConnectionState state);

/**
 * @brief 第 attempt 次重试前的等待时间（指数退避）
 * @param attempt 重试次数（0 表示第一次重试）
 * @return 等待毫秒数：attempt=0→1000, 1→2000, 2→4000, 3→8000，上限 30000ms
 * @note 纯函数，实现见 connectionstate.cpp。
 */
int nextRetryDelayMs(int attempt);

} // namespace domain
} // namespace dscope
