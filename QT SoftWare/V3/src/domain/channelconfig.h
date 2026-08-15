/**
 * @file channelconfig.h
 * @brief V3 领域层 —— 通道配置值类型（header-only，无 .cpp）
 *
 * ── 开发思路 ──
 * 一个物理通道在软件里的"元数据"收敛到 ChannelConfig：名称、单位、量程上下限。
 * 按 V3 权威规格精简为 4 个字段：不做序号、不做原始码值 → 工程值换算（scale/
 * offset 在 V3 第一阶段不需要，原始码值换算的职责尚未引入）。
 *
 * 设计取舍：
 *   - 纯数据（DTO），不附 isValid/toEngineering 等行为 —— 值对象保持最薄，
 *     校验与换算按需再长出来，避免"最小架构"里塞进用不上的抽象；
 *   - rangeMin/rangeMax 保留默认量程（0~100），供曲线坐标轴、报警阈值参考。
 *
 * ── WPF 对照 ──
 *   ChannelConfig  ↔  C# 的纯 POCO / record（WPF 里做数据绑定的最小载体，
 *                     不带逻辑，逻辑交给 ViewModel / 领域服务）。
 */

#pragma once

#include <QString>

namespace dscope {
namespace domain {

/**
 * @struct ChannelConfig
 * @brief 单通道配置（值类型，纯数据）
 */
struct ChannelConfig
{
    QString name;               ///< 通道名（如 "温度"）
    QString unit;               ///< 工程单位（如 "℃"）
    double  rangeMin = 0.0;     ///< 量程下限（工程值）
    double  rangeMax = 100.0;   ///< 量程上限（工程值）
};

} // namespace domain
} // namespace dscope
