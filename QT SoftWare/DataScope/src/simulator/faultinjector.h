/**
 * @file faultinjector.h
 * @brief 模拟设备异常注入器（V2：把"正常帧"变换为"异常字节流"）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类（对应审查"模拟设备无异常注入能力"）
 * ────────────────────────────────────────────────────────────
 * V1 的 Simulator 只发正常正弦波帧，无法验证主机在真实异常下的健壮性
 * （半包/粘包/CRC 错误/非法帧/断线/设备忙……）。
 * V2 把"异常变换"做成独立的纯逻辑类 FaultInjector：
 *   输入：一帧已构建好的正常协议帧字节；
 *   输出：0..N 段字节流（每段代表一次 write 调用）——
 *         可能是篡改后的帧、拆开的帧、合并的帧，或空（本次不发送）。
 *
 * 为什么必须是纯逻辑（无 TCP/Qt 网络依赖）：
 *   1. 可单测：直接构造正常帧喂进去，断言输出字节符合异常语义，无需起网络；
 *   2. 可复用：SimulatorDevice（网络层）和集成测试都能用同一份注入逻辑；
 *   3. 符合分层：字节变换与"何时写 socket"解耦。
 *
 * ── 支持的异常（FaultType）──
 *   None        无异常（原样返回）
 *   Sticky      粘包：两帧合并为一次发送（考验主机半包重组）
 *   Fragment    分包：一帧拆成两段发送（考验主机缓冲拼接）
 *   CrcError    CRC 错误：篡改帧尾校验字节（考验主机校验拒绝）
 *   IllegalFrame 非法帧：把长度字段改为超大值（考验主机防御性拒绝）
 *   Delay       延迟：指定周期内该帧不发送（考验主机超时处理）
 *   Surge       数据突变：某通道值瞬间跳到超量程值（考验主机报警响应）
 *
 * ── WPF 对照 ──
 *   FaultInjector ↔ 测试替身（Test Double）/ 故障注入器（Fault Injection）
 */

#pragma once

#include <QByteArray>
#include <QVector>
#include <QtGlobal>

namespace datascope {
namespace simulator {

/**
 * @brief 异常类型枚举（每种对应一种"主机必须健壮应对"的故障）
 */
enum class FaultType {
    None,          ///< 无异常
    Sticky,        ///< 粘包（多帧一次发送）
    Fragment,      ///< 分包（一帧分多次发送）
    CrcError,      ///< CRC 校验错误（篡改帧尾）
    IllegalFrame,  ///< 非法帧（破坏长度字段等）
    Delay,         ///< 帧延迟（周期内不发送）
    Surge,         ///< 数据突变（通道值跳变超量程）
    Disconnect     ///< 断线（触发周期内主动断开客户端，测主机自动重连）
};

/**
 * @brief 异常注入配置
 */
struct FaultConfig {
    FaultType type = FaultType::None;  ///< 注入的异常类型
    int       period = 1;              ///< 触发周期：每 period 帧注入一次（Delay/Surge 用）
    int       channelIndex = 0;        ///< 突变作用的通道序号（Surge 用）
    double    surgeValue = 200.0;      ///< 突变目标值（Surge 用，超出量程触发主机报警）
};

/**
 * @class FaultInjector
 * @brief 正常帧 → 异常字节流的纯逻辑变换器
 *
 * 用法：
 *   FaultInjector injector;
 *   injector.setConfig({FaultType::CrcError});
 *   const auto chunks = injector.process(frameSeq, normalFrame);
 *   for (const auto &chunk : chunks) socket->write(chunk);  // 每段一次 write
 */
class FaultInjector
{
public:
    /** @brief 配置异常注入 */
    void setConfig(const FaultConfig &cfg) { m_cfg = cfg; }

    /** @brief 当前异常配置（查询用） */
    FaultConfig config() const { return m_cfg; }

    /**
     * @brief 处理一帧正常帧，返回"应发送的字节流段列表"
     * @param frameSeq    帧序号（0 基，递增；Delay/Surge 按它判断触发周期）
     * @param normalFrame 构建好的正常协议帧字节（含 SOF/FUNC/CMD/LEN/DATA/CRC）
     * @return 0..N 段字节（每段对应一次 socket write）；空向量 = 本次不发送
     */
    QVector<QByteArray> process(int frameSeq, const QByteArray &normalFrame);

private:
    /** @brief 是否触发异常（每 period 帧触发一次） */
    bool shouldTrigger(int frameSeq) const
    { return m_cfg.period <= 0 || frameSeq % m_cfg.period == 0; }

    /** @brief 篡改 CRC：帧尾最后一个字节 +1（产生校验错误） */
    QByteArray corruptCrc(const QByteArray &frame) const;

    /** @brief 破坏长度字段：LEN 改为 0xFFFF（解析器应判非法帧头） */
    QByteArray corruptIllegal(const QByteArray &frame) const;

    /**
     * @brief 突变通道值：把载荷中指定通道的 float 替换为突变值
     *
     * 注意：替换字节后必须重算 CRC，使帧保持"合法但值超量程"——
     * 突变不是通信故障，而是设备采集到异常值，主机应正常收帧并触发报警。
     */
    QByteArray surgeChannel(const QByteArray &frame) const;

    FaultConfig m_cfg;   ///< 异常配置
};

} // namespace simulator
} // namespace datascope
