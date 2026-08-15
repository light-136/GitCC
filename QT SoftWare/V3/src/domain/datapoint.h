/**
 * @file datapoint.h
 * @brief V3 领域层 —— 数据点值类型（header-only，无 .cpp）
 *
 * ── 开发思路 ──
 * 一次采样就是一个 DataPoint：通道号 + 工程值 + 两个时间戳。它是最小、最高频的
 * 数据载体（每帧、每通道都会产生一个），所以做成纯值类型：
 *   - 拷贝即值拷贝，无身份概念，谁持有谁都安全；
 *   - 无虚函数、无指针、无资源，可放心放进 QVector / 按值传参；
 *   - 全字段默认成员初始化，默认构造即可用（不会出现未初始化脏数据）。
 *
 * 两个时间戳的分工（这是 V3 权威规格的关键点）：
 *   deviceTs       —— 设备端打的时间戳，表示"设备何时采样"；第一阶段 Simulator
 *                     帧内暂不含该字段，默认构造即无效时间（isNull()）。
 *   hostArrivalTs  —— 主机端打的时间戳，表示"主机何时收到这一帧"，由采集线程
 *                     在收包时填写。设备时钟与主机时钟可能不同步，把两个时刻分开
 *                     记录，后续做延迟统计/时钟漂移分析才有依据。
 *
 * 用"默认构造"表示无效时间：QDateTime 默认构造即 isNull()，比单独加 bool valid
 * 标志更省字段、语义更 Qt 化。调用方用 ts.isValid() 判断是否真的填写过。
 *
 * ── WPF 对照 ──
 *   struct DataPoint  ↔  C# 的 record / POCO 值对象：结构体按值拷贝、无引用语义，
 *                         对应 WPF 里用 record struct 承载"一帧采样"的惯用法。
 */

#pragma once

#include <QDateTime>

namespace dscope {
namespace domain {

/**
 * @struct DataPoint
 * @brief 一次采样的数据点（值类型）
 */
struct DataPoint
{
    int       channelIndex  = 0;    ///< 所属通道序号（0 基）
    double    value         = 0.0;  ///< 工程值（量纲值，可直接显示/报警）
    QDateTime deviceTs;             ///< 设备时间戳（设备采样时刻；第一阶段 Simulator 帧内暂不含，默认无效时间）
    QDateTime hostArrivalTs;        ///< 主机到达时间戳（主机收到帧的时刻，由采集线程填写）
};

} // namespace domain
} // namespace dscope
