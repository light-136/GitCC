using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Services.Report
{
    /// <summary>
    /// 产量报表数据行。
    /// 记录某时段内的生产产量汇总信息。
    /// </summary>
    public class ProductionReportRow
    {
        /// <summary>时段（如 2026-04-29 08:00~09:00）</summary>
        public string Period { get; set; } = "";

        /// <summary>计划产量</summary>
        public int PlannedQty { get; set; }

        /// <summary>实际产量</summary>
        public int ActualQty { get; set; }

        /// <summary>合格品数量</summary>
        public int PassQty { get; set; }

        /// <summary>不合格品数量</summary>
        public int FailQty { get; set; }

        /// <summary>良品率（百分比）</summary>
        public double PassRate => ActualQty > 0 ? Math.Round(PassQty * 100.0 / ActualQty, 2) : 0;

        /// <summary>产能达成率（百分比）</summary>
        public double AchieveRate => PlannedQty > 0 ? Math.Round(ActualQty * 100.0 / PlannedQty, 2) : 0;
    }

    /// <summary>
    /// 报警统计数据行。
    /// 汇总某时段内各级别报警的发生次数和平均处理时长。
    /// </summary>
    public class AlarmStatRow
    {
        /// <summary>报警代码</summary>
        public string Code { get; set; } = "";

        /// <summary>报警描述</summary>
        public string Description { get; set; } = "";

        /// <summary>报警级别</summary>
        public string Level { get; set; } = "";

        /// <summary>发生次数</summary>
        public int Count { get; set; }

        /// <summary>平均处理时长（分钟）</summary>
        public double AvgHandleMinutes { get; set; }
    }

    /// <summary>
    /// 系统运行汇总统计。
    /// 提供仪表盘级别的关键指标（KPI）快照。
    /// </summary>
    public class SystemSummary
    {
        /// <summary>统计时间范围（如"今日"、"本周"）</summary>
        public string Period { get; set; } = "";

        /// <summary>总产量（合格+不合格）</summary>
        public int TotalProduction { get; set; }

        /// <summary>合格品数量</summary>
        public int TotalPass { get; set; }

        /// <summary>综合良品率</summary>
        public double OverallPassRate => TotalProduction > 0
            ? Math.Round(TotalPass * 100.0 / TotalProduction, 2) : 0;

        /// <summary>报警总次数</summary>
        public int TotalAlarms { get; set; }

        /// <summary>严重报警次数（Critical）</summary>
        public int CriticalAlarms { get; set; }

        /// <summary>设备运行时长（小时）</summary>
        public double RunningHours { get; set; }

        /// <summary>OEE（设备综合效率，0~100）</summary>
        public double OeePercent { get; set; }

        /// <summary>生成时间</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 报表服务。
    /// 负责聚合生产数据、报警数据，生成各类报表和 KPI 汇总。
    /// 当前版本使用模拟数据，后续可对接真实数据库查询。
    /// </summary>
    public class ReportService
    {
        private readonly ILoggingService? _logger;
        private readonly Random _rnd = new();

        /// <summary>
        /// 创建报表服务实例。
        /// </summary>
        /// <param name="logger">日志服务（可选）</param>
        public ReportService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// 生成产量报表（按小时汇总）。
        /// </summary>
        /// <param name="date">统计日期（默认今日）</param>
        /// <param name="hours">统计小时数（默认 8 小时）</param>
        /// <returns>每小时一行的产量报表列表</returns>
        public List<ProductionReportRow> GetProductionReport(DateTime? date = null, int hours = 8)
        {
            var baseDate = (date ?? DateTime.Today).Date;
            var rows = new List<ProductionReportRow>();

            for (int i = 0; i < hours; i++)
            {
                var planned = _rnd.Next(80, 120);
                var actual  = _rnd.Next(70, planned + 5);
                var fail    = _rnd.Next(0, Math.Max(1, actual / 10));
                var pass    = actual - fail;

                rows.Add(new ProductionReportRow
                {
                    Period     = $"{baseDate.AddHours(i):HH:mm}~{baseDate.AddHours(i + 1):HH:mm}",
                    PlannedQty = planned,
                    ActualQty  = actual,
                    PassQty    = pass,
                    FailQty    = fail
                });
            }

            _logger?.LogInfo($"生成产量报表：{baseDate:yyyy-MM-dd}，共 {hours} 行", "ReportService");
            return rows;
        }

        /// <summary>
        /// 生成报警统计报表，汇总各报警代码的发生频次和处理时长。
        /// </summary>
        /// <param name="topN">返回频次最高的前 N 个报警（默认 10）</param>
        /// <returns>报警统计行列表（按发生次数降序）</returns>
        public List<AlarmStatRow> GetAlarmStatistics(int topN = 10)
        {
            // 预定义的典型工业报警代码库（演示数据）
            var alarmPool = new[]
            {
                ("E001", "温度超限",   "Critical"),
                ("E002", "气压不足",   "Warning"),
                ("E003", "电机过流",   "Critical"),
                ("W001", "润滑低位",   "Warning"),
                ("W002", "传感器异常", "Warning"),
                ("I001", "换料提示",   "Info"),
                ("E004", "安全门开",   "Critical"),
                ("W003", "编码器误差", "Warning"),
                ("E005", "急停触发",   "Critical"),
                ("I002", "保养提醒",   "Info"),
                ("W004", "振动超限",   "Warning"),
                ("E006", "过载保护",   "Critical"),
            };

            var rows = alarmPool.Take(topN).Select(a => new AlarmStatRow
            {
                Code               = a.Item1,
                Description        = a.Item2,
                Level              = a.Item3,
                Count              = _rnd.Next(1, 30),
                AvgHandleMinutes   = Math.Round(_rnd.NextDouble() * 15 + 1, 1)
            })
            .OrderByDescending(r => r.Count)
            .ToList();

            _logger?.LogInfo($"生成报警统计报表，共 {rows.Count} 类报警", "ReportService");
            return rows;
        }

        /// <summary>
        /// 生成系统运行综合汇总（KPI 仪表盘数据）。
        /// </summary>
        /// <param name="period">统计周期描述（如"今日"、"本周"）</param>
        /// <returns>系统综合运行统计</returns>
        public SystemSummary GetSystemSummary(string period = "今日")
        {
            var totalProduction = _rnd.Next(500, 1000);
            var totalPass       = (int)(totalProduction * (_rnd.NextDouble() * 0.1 + 0.88));
            var runningHours    = Math.Round(_rnd.NextDouble() * 4 + 6, 1);

            var summary = new SystemSummary
            {
                Period          = period,
                TotalProduction = totalProduction,
                TotalPass       = totalPass,
                TotalAlarms     = _rnd.Next(5, 30),
                CriticalAlarms  = _rnd.Next(0, 5),
                RunningHours    = runningHours,
                OeePercent      = Math.Round(75 + _rnd.NextDouble() * 20, 1),
                GeneratedAt     = DateTime.Now
            };

            _logger?.LogInfo($"生成系统汇总 [{period}]：产量={summary.TotalProduction}，OEE={summary.OeePercent}%", "ReportService");
            return summary;
        }

        /// <summary>
        /// 导出产量报表为 CSV 格式字符串，可直接保存为 .csv 文件。
        /// </summary>
        /// <param name="rows">产量报表数据</param>
        /// <returns>UTF-8 CSV 字符串（含表头）</returns>
        public string ExportToCsv(List<ProductionReportRow> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("时段,计划产量,实际产量,合格品,不合格品,良品率(%),达成率(%)");

            foreach (var row in rows)
            {
                sb.AppendLine($"{row.Period},{row.PlannedQty},{row.ActualQty}," +
                              $"{row.PassQty},{row.FailQty},{row.PassRate},{row.AchieveRate}");
            }

            return sb.ToString();
        }
    }
}
