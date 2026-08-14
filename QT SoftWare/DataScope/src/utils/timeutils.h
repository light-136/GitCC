/**
 * @file timeutils.h
 * @brief 时间工具库（P3：Qt 核心类型与隐式共享的载体）
 *
 * 为什么用这个类作为 P3 的代码载体：
 *   - 全部是纯函数（不碰 UI/IO），边界清晰，适合练习 QDateTime / QTime 等
 *     Qt 核心值类型的使用；
 *   - 是后续数据打点、日志/记录文件命名、耗时统计的公共依赖；
 *   - 对应 C# 里的 DateTime / TimeSpan 静态工具类（DateTime.Now、TryParse 等）。
 *
 * 教学点（阅读时注意）：
 *   1. `const QDateTime &dt` —— const 引用入参：QDateTime 是"隐式共享"类型，
 *      传引用避免无谓的引用计数增减，也不修改调用方数据；
 *   2. QStringLiteral / QLatin1String —— 编译期字面量，避免运行时编码转换；
 *   3. `bool parseTime(..., QTime &out)` —— TryParse 风格：返回值报告成败、
 *      出参引用带回结果，对应 C# 的 DateTime.TryParse；
 *   4. msecsTo 的"方向语义"：start.msecsTo(end) = end - start，结果可为负。
 */

#pragma once

#include <QDateTime>
#include <QTime>
#include <QString>
#include <QtGlobal>

namespace datascope {
namespace utils {

/**
 * @class TimeUtils
 * @brief 时间格式化、解析、间隔换算工具
 *
 * 全部为静态方法（工具类不需要实例化，对应 C# 的 static class）。
 */
class TimeUtils
{
public:
    /**
     * @brief 把时刻格式化为数据打点串 "yyyy-MM-dd HH:mm:ss.zzz"（带毫秒）
     * @param dt 要格式化的时刻（const 引用，不拷贝）
     * @return 形如 "2026-08-14 09:30:12.123" 的字符串
     * @note .zzz 固定输出 3 位毫秒；不足 3 位自动补零，保证数据列宽对齐
     */
    static QString formatDateTime(const QDateTime &dt);

    /**
     * @brief 当前时刻格式化，等价 formatDateTime(QDateTime::currentDateTime())
     * @return 当前系统时间的数据打点串
     * @note 数据采集打点、日志时间戳的快捷入口
     */
    static QString nowString();

    /**
     * @brief 两个时刻的间隔（微秒）
     * @param start 起始时刻
     * @param end   结束时刻
     * @return end - start 的微秒数；end 早于 start 时为负，相同时刻为 0
     * @note 用 msecsTo 得到毫秒差再 ×1000 换算成微秒
     */
    static qint64 elapsedMicros(const QDateTime &start, const QDateTime &end);

    /**
     * @brief 文件友好时间戳 "yyyyMMdd_HHmmss_zzz"（无分隔符，可直接作文件名）
     * @param dt 要格式化的时刻
     * @return 形如 "20260814_093012_123"
     * @note 无空格、无冒号、无连字符等非法文件名字符，跨平台安全
     */
    static QString fileTimestamp(const QDateTime &dt);

    /**
     * @brief 解析 "HH:mm:ss[.zzz]" 形式的时刻串
     * @param text 输入文本，如 "09:30:12" 或 "09:30:12.123"
     * @param out  出参引用：解析成功时写入 QTime，失败时不修改
     * @return 解析成功返回 true；失败返回 false（TryParse 风格）
     * @note 先按 "HH:mm:ss" 试解析，失败（如带毫秒）再按 "HH:mm:ss.zzz" 试
     */
    static bool parseTime(const QString &text, QTime &out);

    /**
     * @brief 把秒数格式化为时长串
     * @param seconds 总秒数（非负；负数按 0 处理）
     * @return 不足 1 小时返回 "MM:SS"（如 59 秒 → "00:59"）；
     *         1 小时及以上返回 "HH:MM:SS"（如 3600 → "01:00:00"）
     * @note 对应 C# 的 TimeSpan.ToString("hh\\:mm\\:ss")
     */
    static QString formatDuration(qint64 seconds);
};

} // namespace utils
} // namespace datascope
