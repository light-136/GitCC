// ============================================================
// 文件：LogViewModel.cs
// 用途：系统日志查看页面ViewModel
// 设计思路：
//   系统日志是设备运行状态的"黑匣子"。
//   此页面提供：
//   1. 实时日志流 — 滚动显示最新日志
//   2. 级别筛选 — 按Debug/Info/Warning/Error/Fatal筛选
//   3. 来源筛选 — 按模块来源筛选
//   4. 关键字搜索 — 搜索日志内容
//   5. 导出功能 — 导出日志到文件
// ============================================================

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 系统日志查看页面ViewModel。
    /// </summary>
    public partial class LogViewModel : ObservableObject
    {
        private readonly ILogService _logService;

        [ObservableProperty]
        private string _filterLevel = "全部";

        [ObservableProperty]
        private string _filterSource = "";

        [ObservableProperty]
        private string _searchKeyword = "";

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private int _totalCount;

        /// <summary>筛选后的日志列表</summary>
        public ObservableCollection<LogEntry> FilteredLogs { get; } = new();

        /// <summary>所有日志（缓存）</summary>
        private readonly List<LogEntry> _allLogs = new();

        /// <summary>可选级别</summary>
        public ObservableCollection<string> LevelOptions { get; } = new()
        {
            "全部", "Debug", "Info", "Warning", "Error", "Fatal"
        };

        public LogViewModel(ILogService logService)
        {
            _logService = logService;

            _logService.LogAdded += (_, entry) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _allLogs.Add(entry);
                    TotalCount = _allLogs.Count;

                    if (MatchesFilter(entry))
                    {
                        FilteredLogs.Add(entry);
                        if (FilteredLogs.Count > 2000)
                            FilteredLogs.RemoveAt(0);
                    }
                });
            };
        }

        /// <summary>应用筛选条件</summary>
        [RelayCommand]
        private void ApplyFilter()
        {
            FilteredLogs.Clear();
            foreach (var log in _allLogs)
            {
                if (MatchesFilter(log))
                    FilteredLogs.Add(log);
            }
        }

        /// <summary>清除日志</summary>
        [RelayCommand]
        private void ClearLogs()
        {
            _allLogs.Clear();
            FilteredLogs.Clear();
            TotalCount = 0;
        }

        /// <summary>导出日志到文件</summary>
        [RelayCommand]
        private void ExportLogs()
        {
            var path = $"Logs/Export_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            Directory.CreateDirectory("Logs");
            var lines = _allLogs.Select(l =>
                $"{l.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{l.Level}] {l.Source} - {l.Message}");
            File.WriteAllLines(path, lines);
            _logService.Log(Domain.Enums.LogLevel.Info, "日志管理", $"日志已导出到 {path}，共 {_allLogs.Count} 条");
        }

        private bool MatchesFilter(LogEntry entry)
        {
            if (FilterLevel != "全部" && entry.Level.ToString() != FilterLevel)
                return false;
            if (!string.IsNullOrEmpty(FilterSource) &&
                !entry.Source.Contains(FilterSource, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(SearchKeyword) &&
                !entry.Message.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }
}
