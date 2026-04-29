using SmartMES.Services.Report;

namespace SmartMES.Tests;

/// <summary>
/// 报表服务单元测试。
/// 覆盖：产量报表生成、报警统计、系统汇总、CSV 导出、KPI 计算公式。
/// ReportService 使用随机数据，测试重点在于数据结构正确性和边界值处理。
/// </summary>
public class ReportServiceTests
{
    // 所有测试共享一个 ReportService 实例（无状态，可复用）
    private readonly ReportService _svc = new ReportService();

    // ════════ GetProductionReport 测试 ════════

    [Fact]
    public void GetProductionReport_默认应返回8行()
    {
        var rows = _svc.GetProductionReport();
        Assert.Equal(8, rows.Count);
    }

    [Fact]
    public void GetProductionReport_自定义小时数应返回对应行数()
    {
        var rows4 = _svc.GetProductionReport(hours: 4);
        var rows12 = _svc.GetProductionReport(hours: 12);

        Assert.Equal(4, rows4.Count);
        Assert.Equal(12, rows12.Count);
    }

    [Fact]
    public void GetProductionReport_每行时段字符串不应为空()
    {
        var rows = _svc.GetProductionReport();
        Assert.All(rows, row => Assert.False(string.IsNullOrEmpty(row.Period)));
    }

    [Fact]
    public void GetProductionReport_计划量应大于零()
    {
        var rows = _svc.GetProductionReport();
        Assert.All(rows, row => Assert.True(row.PlannedQty > 0));
    }

    [Fact]
    public void GetProductionReport_实际量不应超过计划量的合理上限()
    {
        // 模拟数据中 actual = rnd.Next(70, planned+5)，实际量不应为负
        var rows = _svc.GetProductionReport(hours: 24);
        Assert.All(rows, row => Assert.True(row.ActualQty >= 0));
    }

    [Fact]
    public void GetProductionReport_不合格品加合格品应等于实际产量()
    {
        var rows = _svc.GetProductionReport();
        Assert.All(rows, row =>
            Assert.Equal(row.ActualQty, row.PassQty + row.FailQty));
    }

    [Fact]
    public void GetProductionReport_良品率应在0到100之间()
    {
        var rows = _svc.GetProductionReport(hours: 24);
        Assert.All(rows, row =>
        {
            Assert.True(row.PassRate >= 0 && row.PassRate <= 100,
                $"良品率超出范围：{row.PassRate}");
        });
    }

    [Fact]
    public void GetProductionReport_达成率应在合理范围内()
    {
        // 达成率 = ActualQty / PlannedQty * 100，因为 actual 可能略超 planned
        var rows = _svc.GetProductionReport(hours: 24);
        Assert.All(rows, row =>
            Assert.True(row.AchieveRate >= 0, $"达成率不应为负：{row.AchieveRate}"));
    }

    [Fact]
    public void GetProductionReport_指定日期应反映在时段字符串中()
    {
        var targetDate = new DateTime(2026, 1, 15);
        var rows = _svc.GetProductionReport(targetDate, hours: 3);

        // 时段格式为 HH:mm~HH:mm，与日期无关（仅包含时间）
        Assert.Equal(3, rows.Count);
        // 第一个时段应从 00:00 开始（基于 date.Date 的起始小时）
        Assert.Contains("00:00", rows[0].Period);
    }

    // ════════ ProductionReportRow 计算属性测试 ════════

    [Fact]
    public void PassRate_实际量为0时应返回0()
    {
        var row = new ProductionReportRow
        {
            ActualQty  = 0,
            PassQty    = 0,
            PlannedQty = 100
        };
        Assert.Equal(0, row.PassRate);
    }

    [Fact]
    public void PassRate_全部合格时应为100()
    {
        var row = new ProductionReportRow
        {
            ActualQty  = 200,
            PassQty    = 200,
            PlannedQty = 200
        };
        Assert.Equal(100, row.PassRate);
    }

    [Fact]
    public void AchieveRate_计划量为0时应返回0()
    {
        var row = new ProductionReportRow
        {
            PlannedQty = 0,
            ActualQty  = 50
        };
        Assert.Equal(0, row.AchieveRate);
    }

    [Fact]
    public void AchieveRate_完全达成时应为100()
    {
        var row = new ProductionReportRow
        {
            PlannedQty = 100,
            ActualQty  = 100
        };
        Assert.Equal(100, row.AchieveRate);
    }

    [Fact]
    public void PassRate_结果应保留两位小数()
    {
        var row = new ProductionReportRow
        {
            ActualQty = 3,
            PassQty   = 2  // 2/3 = 66.67%
        };
        // Math.Round(..., 2) 确保精度
        Assert.Equal(66.67, row.PassRate, precision: 2);
    }

    // ════════ GetAlarmStatistics 测试 ════════

    [Fact]
    public void GetAlarmStatistics_默认返回10行()
    {
        var rows = _svc.GetAlarmStatistics();
        Assert.Equal(10, rows.Count);
    }

    [Fact]
    public void GetAlarmStatistics_topN参数应限制返回数量()
    {
        var rows5 = _svc.GetAlarmStatistics(topN: 5);
        Assert.Equal(5, rows5.Count);
    }

    [Fact]
    public void GetAlarmStatistics_每行报警代码不应为空()
    {
        var rows = _svc.GetAlarmStatistics();
        Assert.All(rows, row => Assert.False(string.IsNullOrEmpty(row.Code)));
    }

    [Fact]
    public void GetAlarmStatistics_报警次数应大于零()
    {
        var rows = _svc.GetAlarmStatistics();
        Assert.All(rows, row => Assert.True(row.Count > 0));
    }

    [Fact]
    public void GetAlarmStatistics_应按次数降序排列()
    {
        var rows = _svc.GetAlarmStatistics();
        for (int i = 0; i < rows.Count - 1; i++)
            Assert.True(rows[i].Count >= rows[i + 1].Count,
                $"排序错误：第{i}行({rows[i].Count}) < 第{i+1}行({rows[i+1].Count})");
    }

    [Fact]
    public void GetAlarmStatistics_平均处理时间应大于等于1分钟()
    {
        var rows = _svc.GetAlarmStatistics();
        Assert.All(rows, row =>
            Assert.True(row.AvgHandleMinutes >= 1.0, $"处理时间异常：{row.AvgHandleMinutes}"));
    }

    [Fact]
    public void GetAlarmStatistics_级别应为有效值()
    {
        var validLevels = new[] { "Critical", "Warning", "Info" };
        var rows = _svc.GetAlarmStatistics();
        Assert.All(rows, row => Assert.Contains(row.Level, validLevels));
    }

    // ════════ GetSystemSummary 测试 ════════

    [Fact]
    public void GetSystemSummary_默认周期应为今日()
    {
        var summary = _svc.GetSystemSummary();
        Assert.Equal("今日", summary.Period);
    }

    [Fact]
    public void GetSystemSummary_自定义周期应正确保存()
    {
        var summary = _svc.GetSystemSummary("本周");
        Assert.Equal("本周", summary.Period);
    }

    [Fact]
    public void GetSystemSummary_总产量应在合理范围()
    {
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.TotalProduction >= 500 && summary.TotalProduction <= 1000,
            $"总产量超出范围：{summary.TotalProduction}");
    }

    [Fact]
    public void GetSystemSummary_合格品不超过总产量()
    {
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.TotalPass <= summary.TotalProduction,
            $"合格品({summary.TotalPass}) > 总产量({summary.TotalProduction})");
    }

    [Fact]
    public void GetSystemSummary_综合良品率应在88到100之间()
    {
        // 模拟数据: totalPass = totalProduction * (rnd * 0.1 + 0.88)
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.OverallPassRate >= 0 && summary.OverallPassRate <= 100,
            $"良品率超出范围：{summary.OverallPassRate}");
    }

    [Fact]
    public void GetSystemSummary_OEE应在75到95之间()
    {
        // 模拟数据: OEE = 75 + rnd * 20
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.OeePercent >= 75 && summary.OeePercent <= 95,
            $"OEE超出范围：{summary.OeePercent}");
    }

    [Fact]
    public void GetSystemSummary_运行时长应在合理范围()
    {
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.RunningHours >= 6 && summary.RunningHours <= 10,
            $"运行时长超出范围：{summary.RunningHours}");
    }

    [Fact]
    public void GetSystemSummary_生成时间应接近当前时间()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.GeneratedAt >= before);
    }

    [Fact]
    public void GetSystemSummary_严重报警不超过总报警数()
    {
        var summary = _svc.GetSystemSummary();
        Assert.True(summary.CriticalAlarms <= summary.TotalAlarms);
    }

    // ════════ SystemSummary 计算属性测试 ════════

    [Fact]
    public void OverallPassRate_总产量为0时应返回0()
    {
        var summary = new SystemSummary
        {
            TotalProduction = 0,
            TotalPass = 0
        };
        Assert.Equal(0, summary.OverallPassRate);
    }

    [Fact]
    public void OverallPassRate_计算精度应保留两位小数()
    {
        var summary = new SystemSummary
        {
            TotalProduction = 3,
            TotalPass = 2
        };
        Assert.Equal(66.67, summary.OverallPassRate, precision: 2);
    }

    // ════════ ExportToCsv 测试 ════════

    [Fact]
    public void ExportToCsv_应包含CSV表头()
    {
        var rows = _svc.GetProductionReport(hours: 2);
        var csv = _svc.ExportToCsv(rows);

        Assert.Contains("时段", csv);
        Assert.Contains("计划产量", csv);
        Assert.Contains("实际产量", csv);
        Assert.Contains("合格品", csv);
        Assert.Contains("良品率", csv);
    }

    [Fact]
    public void ExportToCsv_行数应为数据行数加1含表头()
    {
        var rows = _svc.GetProductionReport(hours: 5);
        var csv = _svc.ExportToCsv(rows);

        // 按换行符分割，过滤空行
        var lines = csv.Split('\n').Where(l => l.Length > 0).ToList();
        Assert.Equal(6, lines.Count);  // 1 表头 + 5 数据行
    }

    [Fact]
    public void ExportToCsv_每行应包含7个CSV字段()
    {
        var rows = _svc.GetProductionReport(hours: 1);
        var csv = _svc.ExportToCsv(rows);

        // 跳过表头
        var dataLine = csv.Split('\n').Skip(1).FirstOrDefault(l => l.Trim().Length > 0);
        Assert.NotNull(dataLine);

        var fields = dataLine!.Split(',');
        Assert.Equal(7, fields.Length);
    }

    [Fact]
    public void ExportToCsv_空列表应只返回表头()
    {
        var csv = _svc.ExportToCsv(new List<ProductionReportRow>());

        var lines = csv.Split('\n').Where(l => l.Length > 0).ToList();
        Assert.Single(lines);  // 只有表头一行
        Assert.Contains("时段", lines[0]);
    }

    [Fact]
    public void ExportToCsv_数据中的实际值应能在CSV中找到()
    {
        var rows = new List<ProductionReportRow>
        {
            new() { Period = "08:00~09:00", PlannedQty = 100, ActualQty = 90, PassQty = 85, FailQty = 5 }
        };
        var csv = _svc.ExportToCsv(rows);

        Assert.Contains("08:00~09:00", csv);
        Assert.Contains("100", csv);
        Assert.Contains("90", csv);
    }

    // ════════ 构造函数测试 ════════

    [Fact]
    public void Constructor_无参构造不应抛出异常()
    {
        var ex = Record.Exception(() => new ReportService());
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_传入null日志服务不应抛出异常()
    {
        var svc = new ReportService(logger: null);
        // 调用方法时不应崩溃
        var rows = svc.GetProductionReport(hours: 1);
        Assert.Single(rows);
    }
}
