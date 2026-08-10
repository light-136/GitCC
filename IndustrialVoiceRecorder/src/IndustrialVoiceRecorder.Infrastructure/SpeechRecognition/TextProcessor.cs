using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;

/// <summary>
/// 文本处理器
/// 对语音识别结果进行后处理：
/// 1. 提取产品编号
/// 2. 判定OK/NG状态
/// 3. 分类归属
/// 4. 关键词纠正
/// </summary>
public class TextProcessor
{
    private readonly ILogger<TextProcessor> _logger;

    /// <summary>
    /// 分类关键词映射表
    /// 用于根据文本内容自动分类
    /// </summary>
    private static readonly Dictionary<string, string[]> CategoryKeywords = new()
    {
        { "尺寸", new[] { "尺寸", "大小", "长度", "宽度", "厚度", "直径", "细小", "超差", "偏差", "公差" } },
        { "外观", new[] { "外观", "表面", "划痕", "灰尘", "污渍", "瑕疵", "毛刺", "划伤", "磨损", "变形", "凹陷", "鼓包" } },
        { "颜色", new[] { "颜色", "色差", "褪色", "发黄", "发白", "变色" } },
        { "丝印", new[] { "丝印", "印刷", "字体", "模糊", "偏移", "脱落" } },
        { "功能", new[] { "功能", "性能", "测试", "失败", "异常", "故障" } },
        { "包装", new[] { "包装", "标签", "条码", "扫码", "贴标" } },
        { "操作", new[] { "操作", "动作", "行为", "规范", "流程", "工序" } }
    };

    /// <summary>
    /// NG关键词列表
    /// 包含这些词的文本会被标记为NG
    /// </summary>
    private static readonly string[] NgKeywords = new[]
    {
        "NG", "不合格", "异常", "问题", "缺陷", "不良", "报废",
        "划痕", "灰尘", "毛刺", "超差", "偏差", "损坏", "破损",
        "变形", "脱落", "污渍", "色差", "划伤", "凹陷", "鼓包",
        "失败", "故障", "不规范", "错误"
    };

    /// <summary>
    /// 工业专业词汇纠正映射表
    /// 用于修正常见的识别错误
    /// </summary>
    private static readonly Dictionary<string, string> CorrectionMap = new()
    {
        { "硅胶", "硅胶" },
        { "思印", "丝印" },
        { "似印", "丝印" },
        { "思银", "丝印" },
        { "恩鸡", "NG" },
        { "恩基", "NG" },
        { "嗯鸡", "NG" },
        { "OK的", "OK" },
        { "哦开", "OK" },
        { "欧开", "OK" },
    };

    public TextProcessor(ILogger<TextProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理识别文本
    /// 对原始识别文本进行分析和结构化
    /// </summary>
    public ProcessedRecord Process(string text)
    {
        try
        {
            // 第一步：词汇纠正
            string correctedText = CorrectText(text);

            // 第二步：提取产品编号
            string? productId = ExtractProductId(correctedText);

            // 第三步：判定状态
            string status = DetermineStatus(correctedText);

            // 第四步：分类
            string category = DetermineCategory(correctedText);

            // 第五步：提取备注
            string? remarks = ExtractRemarks(correctedText, status);

            var result = new ProcessedRecord
            {
                OriginalText = text,
                CorrectedText = correctedText,
                ProductId = productId,
                Status = status,
                Category = category,
                Remarks = remarks
            };

            _logger.LogDebug("文本处理完成: 原文=\"{Original}\" -> 产品={ProductId}, 状态={Status}, 分类={Category}",
                text, productId, status, category);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文本处理失败: {Text}", text);
            return new ProcessedRecord
            {
                OriginalText = text,
                CorrectedText = text,
                Status = "未知",
                Category = "其他"
            };
        }
    }

    /// <summary>
    /// 词汇纠正
    /// 根据纠正映射表修正识别错误
    /// </summary>
    private string CorrectText(string text)
    {
        string result = text;
        foreach (var (wrong, correct) in CorrectionMap)
        {
            result = result.Replace(wrong, correct, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// 提取产品编号
    /// 支持多种格式：1号、第1个、1#、产品1
    /// </summary>
    private string? ExtractProductId(string text)
    {
        // 匹配模式：数字+号、第+数字+个/号、数字+#
        var patterns = new[]
        {
            @"(\d+)\s*号",        // "1号"、"15号"
            @"第\s*(\d+)\s*[个号]", // "第1个"、"第15号"
            @"(\d+)\s*#",          // "1#"、"15#"
            @"产品\s*(\d+)",       // "产品1"、"产品15"
            @"^(\d+)\s*[,，]",     // "1，OK"（以数字开头）
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 判定状态
    /// 根据NG关键词列表判定OK/NG
    /// </summary>
    private string DetermineStatus(string text)
    {
        // 检查是否包含NG关键词
        foreach (var keyword in NgKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return "NG";
            }
        }

        // 默认为OK
        return "OK";
    }

    /// <summary>
    /// 判定分类
    /// 根据关键词映射表自动分类
    /// </summary>
    private string DetermineCategory(string text)
    {
        foreach (var (category, keywords) in CategoryKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }
        }

        return "其他";
    }

    /// <summary>
    /// 提取备注
    /// 如果是NG状态，提取具体原因
    /// </summary>
    private string? ExtractRemarks(string text, string status)
    {
        if (status != "NG")
            return null;

        // 提取NG原因（检测到的关键词）
        var reasons = new List<string>();
        foreach (var keyword in NgKeywords)
        {
            if (keyword != "NG" && keyword != "不合格" &&
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(keyword);
            }
        }

        return reasons.Count > 0 ? string.Join("、", reasons) : "异常";
    }
}

/// <summary>
/// 处理后的记录
/// 包含从原始文本中提取的结构化信息
/// </summary>
public class ProcessedRecord
{
    /// <summary>
    /// 原始文本
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// 纠正后的文本
    /// </summary>
    public string CorrectedText { get; set; } = string.Empty;

    /// <summary>
    /// 产品编号
    /// </summary>
    public string? ProductId { get; set; }

    /// <summary>
    /// 状态（OK/NG）
    /// </summary>
    public string Status { get; set; } = "OK";

    /// <summary>
    /// 分类
    /// </summary>
    public string Category { get; set; } = "其他";

    /// <summary>
    /// 备注（NG原因）
    /// </summary>
    public string? Remarks { get; set; }
}
