using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.ViewModels
{
    /// <summary>
    /// 绯荤粺鏃ュ織ViewModel
    /// 瀹炴椂鏄剧ずInfo/Warning/Error/Communication鍥涚被鏃ュ織
    /// </summary>
    public class LogViewModel : ViewModelBase
    {
        private readonly ILoggingService _logger;

        public ObservableCollection<LogEntry> Logs { get; } = new();

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set { SetProperty(ref _filterText, value); ApplyFilter(); }
        }

        private LogLevel? _selectedLevel;
        public LogLevel? SelectedLevel
        {
            get => _selectedLevel;
            set { SetProperty(ref _selectedLevel, value); ApplyFilter(); }
        }

        public ObservableCollection<LogEntry> FilteredLogs { get; } = new();

        public RelayCommand ClearCommand { get; }
        public RelayCommand CopyCommand { get; }

        /// <summary>
        /// 自动补齐：LogViewModel 方法说明。
        /// </summary>
        public LogViewModel(ILoggingService logger)
        {
            _logger = logger;

            // 璁㈤槄鏃ュ織鏂板浜嬩欢锛屽疄鏃舵帹閫佸埌UI
            _logger.LogAdded += (_, entry) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Logs.Add(entry);
                    ApplyFilter();
                });
            };

            // 鍔犺浇宸叉湁鏃ュ織
            foreach (var log in _logger.GetLogs())
            {
                Logs.Add(log);
                FilteredLogs.Add(log);
            }

            ClearCommand = new RelayCommand(_ =>
            {
                Logs.Clear();
                FilteredLogs.Clear();
            });

            CopyCommand = new RelayCommand(_ =>
            {
                var text = string.Join(Environment.NewLine,
                    FilteredLogs.Select(l => l.ToString()));
                Clipboard.SetText(text);
            });
        }

        /// <summary>
        /// 自动补齐：ApplyFilter 方法说明。
        /// </summary>
        private void ApplyFilter()
        {
            FilteredLogs.Clear();
            var filtered = Logs.AsEnumerable();

            if (_selectedLevel.HasValue)
                filtered = filtered.Where(l => l.Level == _selectedLevel.Value);

            if (!string.IsNullOrWhiteSpace(_filterText))
                filtered = filtered.Where(l =>
                    l.Message.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                    l.Source.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

            foreach (var log in filtered.TakeLast(500))
                FilteredLogs.Add(log);
        }
    }
}
