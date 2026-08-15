/**
 * @file channelconfigcodec.h
 * @brief 通道配置查询/响应的协议载荷编解码声明
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类
 * ────────────────────────────────────────────────────────────
 * 监控页的通道元数据（名称/单位/量程）应由"设备上报"驱动，而非 UI 硬编码。
 * 主机发查询帧 → 设备回通道配置帧 → 主机按真实配置渲染。本 codec 负责把
 * "通道配置列表"与"协议载荷字节"互转，业务层不直接抠字节。
 *
 * ── 帧契约（配合 frametypes.h 中的常量）──
 *   查询请求：FUNC=kFuncQuery(0x03), CMD=kCmdQueryChannels(0x01), 空载荷
 *   查询响应：FUNC=kFuncQueryResponse(0x83), CMD=kCmdQueryChannels(0x01),
 *             载荷 = [通道数:1B] + N×[通道配置:33B]
 *
 *   单通道配置（33 字节，全部定长、无对齐问题）：
 *     | index:1B | name:16B UTF-8 | unit:8B UTF-8 | min:4B float 大端 | max:4B float 大端 |
 *
 *   为什么用"定长 UTF-8"而非 C 风格字符串：
 *     - 定长方便主机预分配与逐通道跳读（载荷长度 = 1 + 33×N 可精确校验）；
 *     - UTF-8 支持中文通道名（"温度"占 6 字节 < 16），超长截断、不足补 0；
 *     - 与帧内 float 的"大端 4 字节"约定保持一致，编解码对称。
 *
 * ── WPF 对照 ──
 *   这个 codec 相当于 C# 的"报文序列化器"（BinaryReader/BinaryWriter 封装），
 *   把通道配置对象 ↔ 线格式字节互转。
 */

#pragma once

#include <QByteArray>
#include <QString>
#include <QVector>
#include <QtGlobal>

#include "protocol/frametypes.h"

namespace dscope {
namespace protocol {

// ---- 通道配置载荷布局常量（本 codec 私有契约）----

/** @brief 通道名字段定长（UTF-8 字节数） */
constexpr int kChannelNameLen = 16;

/** @brief 单位字段定长（UTF-8 字节数） */
constexpr int kChannelUnitLen = 8;

/** @brief 单通道配置的载荷字节数：index1 + name16 + unit8 + min4 + max4 = 33 */
constexpr int kChannelConfigWireSize = 1 + kChannelNameLen + kChannelUnitLen + 4 + 4;

/**
 * @struct ChannelConfigInfo
 * @brief 通道配置的线格式表示（协议层，与领域层 ChannelConfig 解耦）
 *
 * 协议层不依赖领域层：这里只描述"网络字节里长什么样"，
 * 领域层的 ChannelConfig（含 scale/offset 换算等）由上层自行映射。
 */
struct ChannelConfigInfo {
    quint8 index = 0;           ///< 通道序号（0 基，与数据帧通道号对应）
    QString name;               ///< 通道名（UTF-8，如 "温度"）
    QString unit;               ///< 工程单位（UTF-8，如 "℃"）
    float   rangeMin = 0.0f;    ///< 量程下限（float 大端 4 字节）
    float   rangeMax = 100.0f;  ///< 量程上限（float 大端 4 字节）
};

/**
 * @class ChannelConfigCodec
 * @brief 通道配置列表 ↔ 协议载荷字节 的编解码器
 *
 * 用法（设备侧响应）：
 *   QVector<ChannelConfigInfo> cfg; ... 填充通道配置 ...
 *   const QByteArray payload = ChannelConfigCodec::encode(cfg);
 *   write(FrameBuilder::build(kFuncQueryResponse, kCmdQueryChannels, payload));
 *
 * 用法（主机侧解析）：
 *   QVector<ChannelConfigInfo> cfg;
 *   if (ChannelConfigCodec::decode(resp.payload, cfg)) { ... 渲染通道卡片 ... }
 */
class ChannelConfigCodec
{
public:
    /**
     * @brief 通道配置列表 → 载荷字节
     * @param channels 通道配置列表
     * @return 载荷字节；通道数 <1 或 >255 时返回空数组（编码失败）
     */
    static QByteArray encode(const QVector<ChannelConfigInfo> &channels);

    /**
     * @brief 载荷字节 → 通道配置列表
     * @param payload 响应帧的载荷
     * @param out     出参：解析出的通道配置列表
     * @return false = 载荷结构非法（长度不符/通道数为 0），out 保持为空
     */
    static bool decode(const QByteArray &payload, QVector<ChannelConfigInfo> &out);

private:
    /** @brief 编码单通道（index + name + unit + min + max），内部由 encode 复用 */
    static QByteArray encodeChannel(const ChannelConfigInfo &ch);

    /** @brief 解码单通道（严格按定长布局读取） */
    static bool decodeChannel(const QByteArray &payload, int offset, ChannelConfigInfo &out);
};

} // namespace protocol
} // namespace dscope
