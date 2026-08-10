using System.Text;
using System.Text.RegularExpressions;

namespace IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;

/// <summary>
/// 中文数字转换器 v3.4
/// 核心修复：ASR识别带空格导致数字被拆分（如"十 七 点 四五"）
/// 方案：全局清除空格后再正则匹配，保证连续数字不被拆分
///
/// 支持：
///   十七点四五 → 17.45
///   三点五 → 3.5
///   点五 → 0.5
///   八百七十五点一 → 875.1
///   零点零三 → 0.03
///   一二三 → 123
///   幺二三四 → 1234
///   二十三 → 23
///   一千零五 → 1005
/// </summary>
public static class ChineseNumberConverter
{
    // 基础数字映射（含口语幺/俩/仨）
    private static readonly Dictionary<char, int> DigitMap = new()
    {
        { '零', 0 }, { '〇', 0 },
        { '一', 1 }, { '壹', 1 }, { '幺', 1 },
        { '二', 2 }, { '贰', 2 }, { '两', 2 }, { '俩', 2 },
        { '三', 3 }, { '叁', 3 }, { '仨', 3 },
        { '四', 4 }, { '肆', 4 },
        { '五', 5 }, { '伍', 5 },
        { '六', 6 }, { '陆', 6 },
        { '七', 7 }, { '柒', 7 },
        { '八', 8 }, { '捌', 8 },
        { '九', 9 }, { '玖', 9 },
    };

    // 进位单位
    private static readonly Dictionary<char, long> UnitMap = new()
    {
        { '十', 10 }, { '拾', 10 },
        { '百', 100 }, { '佰', 100 },
        { '千', 1000 }, { '仟', 1000 },
        { '万', 10000 },
        { '亿', 100000000 },
    };

    // 纯数字汉字（无单位）
    private static readonly HashSet<char> PureDigitChars = new(
        "零〇一壹二贰两俩三叁仨四肆五伍六陆七柒八捌九玖幺".ToCharArray());

    // 完整中文数字字符集（含"点"）
    private const string AllNumberChars = "零〇一壹二贰两俩三叁仨四肆五伍六陆七柒八捌九玖幺十拾百佰千仟万亿点";
    private static readonly Regex FullNumberRegex = new($"[{Regex.Escape(AllNumberChars)}]+", RegexOptions.Compiled);

    /// <summary>
    /// 对外统一转换入口
    /// 【核心】先全局清除空格，再正则匹配，避免ASR空格拆分数字串
    /// </summary>
    public static string Convert(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 全局清除所有空格/制表/换行，保证连续数字不被拆分
        // "十 七 点 四五" → "十七点四五" → 17.45
        string textNoSpace = text
            .Replace(" ", "")
            .Replace("\t", "")
            .Replace("\r", "")
            .Replace("\n", "");

        // 无空格文本再进行正则匹配替换
        return FullNumberRegex.Replace(textNoSpace, match =>
        {
            string src = match.Value;
            if (string.IsNullOrEmpty(src))
                return match.Value;

            // 最高优先级：带小数点 → 整串统一解析为小数
            if (src.Contains('点'))
                return ConvertDecimalString(src);

            // 无小数点：检查是否有单位（十百千万亿）
            bool hasUnit = src.Any(c => UnitMap.ContainsKey(c));
            if (hasUnit)
            {
                // 有单位：标准整数解析（十七→17，二百三十四→234）
                long? num = ParseChineseInteger(src);
                return num.HasValue ? num.Value.ToString() : src;
            }
            else
            {
                // 无单位纯连读数字：九八→98，幺二三四→1234
                var sb = new StringBuilder();
                foreach (var c in src)
                {
                    if (DigitMap.TryGetValue(c, out var val))
                        sb.Append(val);
                }
                return sb.Length > 0 ? sb.ToString() : src;
            }
        });
    }

    /// <summary>
    /// 处理带「点」的完整小数
    /// 十七点四五 → 17.45、点五 → 0.5、八百七十五点一 → 875.1
    /// </summary>
    private static string ConvertDecimalString(string src)
    {
        string[] parts = src.Split('点', 2);
        string intStr = parts[0];
        string decStr = parts[1];

        // 整数部分解析
        long integerVal = 0;
        if (!string.IsNullOrEmpty(intStr))
        {
            bool intHasUnit = intStr.Any(c => UnitMap.ContainsKey(c));
            if (intHasUnit)
            {
                // 带十/百/千单位：十七→17
                integerVal = ParseChineseInteger(intStr) ?? 0;
            }
            else
            {
                // 纯单字拼接：三→3
                var intSb = new StringBuilder();
                foreach (var c in intStr)
                    if (DigitMap.TryGetValue(c, out var v)) intSb.Append(v);
                long.TryParse(intSb.ToString(), out integerVal);
            }
        }

        // 小数部分逐字拼接（不做进位计算）：四五→45
        var decSb = new StringBuilder();
        foreach (var c in decStr)
        {
            if (DigitMap.TryGetValue(c, out var d))
                decSb.Append(d);
        }

        string decResult = decSb.Length > 0 ? decSb.ToString() : "0";
        return $"{integerVal}.{decResult}";
    }

    /// <summary>
    /// 标准中文整数解析
    /// 十七→17、二百三十四→234、一千零五→1005、十→10
    /// </summary>
    private static long? ParseChineseInteger(string chinese)
    {
        if (string.IsNullOrEmpty(chinese)) return null;

        long total = 0;
        long section = 0;
        long currentDigit = 0;

        foreach (char c in chinese)
        {
            if (DigitMap.TryGetValue(c, out var d))
            {
                currentDigit = d;
            }
            else if (UnitMap.TryGetValue(c, out var unit))
            {
                // "十"前无数字自动补1：十→一十=10
                if (currentDigit == 0 && unit == 10)
                    currentDigit = 1;

                if (unit >= 10000)
                {
                    // 万/亿级
                    section = (section + currentDigit) * unit;
                    total += section;
                    section = 0;
                    currentDigit = 0;
                }
                else
                {
                    // 十/百/千级
                    section += currentDigit * unit;
                    currentDigit = 0;
                }
            }
            else
            {
                return null; // 非法字符
            }
        }

        total += section + currentDigit;
        return total;
    }
}
