using SmartMES.Core.Infrastructure;
using SmartMES.Services.Report;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace SmartMES.UI.Modules.ReportModule
{
    /// <summary>
    /// 报表统计模块 ViewModel。
    /// 提供产量报表、报警统计、系统 KPI 汇总三大视图的数据绑定和命令处理。
    /// 使用 ReportService 获取数据，支持按日期筛选和 CSV 导出。
    /// </summary>
    public class ReportViewModel : ViewModelBase
    {
        // ──────── 依赖服务 ────────
        private readonly ReportService _reportService;

        // ──────── 筛选参数 ────────
        private DateTime _selectedDate = DateTime.Today;
        private string _selectedPeriod = "今日";

        // ──────── 状态属性 ────────
        private string _statusText = "就绪 - 点击刷新加载报表数据";
        private bool _isLoading;

        // ──────── 产量报表 ────────
        /// <summary>产量报表数据列表（按小时汇总）</summary>
        public ObservableCollection<ProductionReportRow> ProductionRows { get; } = new();

        // ──────── 报警统计 ────────
        /// <summary>报警统计数据列表（按发生次数降序）</summary>
        public ObservableCollection<AlarmStatRow> AlarmStatRows { get; } = new();

        // ──────── 系统KPI汇总 ────────
        private SystemSummary _summary = new();

        // ──────── 日志 ────────
        /// <summary>操作日志（最近 200 条）</summary>
        public ObservableCollection<string> Logs { get; } = new();

        // ════════ 绑定属性 ════════

        /// <summary>当前选中的统计日期</summary>
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set => SetProperty(ref _selectedDate, value);
        }

        /// <summary>统计周期选项</summary>
        public ObservableCollection<string> PeriodOptions { get; } = new()
            { "今日", "本周", "本月", "自定义日期" };

        /// <summary>当前选中的统计周期</summary>
        public string SelectedPeriod
        {
            get => _selectedPeriod;
            set => SetProperty(ref _selectedPeriod, value);
        }

        /// <summary>模块状态描述</summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>是否正在加载数据（用于绑定加载动画）</summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // ── KPI 汇总绑定属性 ──

        /// <summary>总产量</summary>
        public int TotalProduction => _summary.TotalProduction;

        /// <summary>综合良品率（百分比）</summary>
        public double OverallPassRate => _summary.OverallPassRate;

        /// <summary>报警总次数</summary>
        public int TotalAlarms => _summary.TotalAlarms;

        /// <summary>严重报警次数</summary>
        public int CriticalAlarms => _summary.CriticalAlarms;

        /// <summary>设备运行时长（小时）</summary>
        public double RunningHours => _summary.RunningHours;

        /// <summary>OEE（设备综合效率，百分比）</summary>
        public double OeePercent => _summary.OeePercent;

        /// <summary>汇总数据生成时间</summary>
        public string GeneratedAt => _summary.GeneratedAt.ToString("MM-dd HH:mm:ss");

        // ════════ 命令 ════════
        /// <summary>刷新所有报表数据</summary>
        public RelayCommand RefreshCommand { get; }

        /// <summary>导出产量报表为 CSV 文件</summary>
        public RelayCommand ExportCsvCommand { get; }

        /// <summary>清空日志</summary>
        public RelayCommand ClearLogCommand { get; }

        /// <summary>
        /// 构造报表 ViewModel，初始化服务和命令。
        /// </summary>
        public ReportViewModel()
        {
            _reportService = new ReportService();

            RefreshCommand   = new RelayCommand(async _ => await RefreshAllAsync(), _ => !_isLoading);
            ExportCsvCommand = new RelayCommand(async _ => await ExportCsvAsync(),  _ => ProductionRows.Count > 0);
            ClearLogCommand  = new RelayCommand(_ => Logs.Clear());

            AddLog("报表统计模块已就绪");
        }

        // ════════ 私有方法 ════════

        /// <summary>
        /// 刷新所有报表数据：产量报表、报警统计、系统汇总。
        /// </summary>
        private async Task RefreshAllAsync()
        {
            IsLoading = true;
            StatusText = "正在加载报表数据...";
            RefreshCommand.RaiseCanExecuteChanged();

            try
            {
                // 在后台线程执行数据聚合（避免卡 UI）
                var productionRows = await Task.Run(() =>
                    _reportService.GetProductionReport(_selectedDate, 8));

                var alarmRows = await Task.Run(() =>
                    _reportService.GetAlarmStatistics(10));

                var summary = await Task.Run(() =>
                    _reportService.GetSystemSummary(_selectedPeriod));

                // 更新 UI 集合必须回到 Dispatcher 线程
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ProductionRows.Clear();
                    foreach (var r in productionRows) ProductionRows.Add(r);

                    AlarmStatRows.Clear();
                    foreach (var r in alarmRows) AlarmStatRows.Add(r);

                    _summary = summary;
                    RefreshSummaryProperties();
                });

                StatusText = $"报表已刷新（{_selectedDate:yyyy-MM-dd}）";
                ExportCsvCommand.RaiseCanExecuteChanged();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 报表刷新完成：产量 {summary.TotalProduction} 件，OEE {summary.OeePercent}%");
            }
            catch (Exception ex)
            {
                StatusText = $"加载失败：{ex.Message}";
                AddLog($"[{DateTime.Now:HH:mm:ss}] 报表加载异常：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 将产量报表导出为 CSV 文件，弹出保存对话框。
        /// </summary>
        private async Task ExportCsvAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title      = "导出产量报表",
                    Filter     = "CSV 文件|*.csv",
                    FileName   = $"产量报表_{_selectedDate:yyyyMMdd}.csv",
                    DefaultExt = ".csv"
                };

                if (dialog.ShowDialog() != true) return;

                var rows = ProductionRows.ToList();
                var csv  = _reportService.ExportToCsv(rows);

                await File.WriteAllTextAsync(dialog.FileName,
                    "﻿" + csv,  // BOM 头，确保 Excel 正确识别 UTF-8
                    System.Text.Encoding.UTF8);

                AddLog($"[{DateTime.Now:HH:mm:ss}] CSV 导出成功：{dialog.FileName}");
                StatusText = $"已导出：{dialog.FileName}";
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] CSV 导出失败：{ex.Message}");
            }
        }

        /// <summary>通知所有 KPI 汇总属性刷新</summary>
        private void RefreshSummaryProperties()
        {
            OnPropertyChanged(nameof(TotalProduction));
            OnPropertyChanged(nameof(OverallPassRate));
            OnPropertyChanged(nameof(TotalAlarms));
            OnPropertyChanged(nameof(CriticalAlarms));
            OnPropertyChanged(nameof(RunningHours));
            OnPropertyChanged(nameof(OeePercent));
            OnPropertyChanged(nameof(GeneratedAt));
        }

        /// <summary>向日志区添加一条记录（最多保留 200 条）</summary>
        private void AddLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Insert(0, message);
                if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
            });
        }
    }
}
