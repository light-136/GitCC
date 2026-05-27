// ============================================================
// 文件：AlarmViewModel.cs
// 用途：报警管理页面ViewModel
// 设计思路：
//   工业设备报警管理要求：
//   1. 活跃报警 — 实时显示当前未处理的报警
//   2. 历史报警 — 查询过去的报警记录
//   3. 报警确认 — 操作员确认并清除报警
//   4. 全部复位 — 一键清除所有活跃报警
//   5. 报警统计 — 按级别统计
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 报警管理页面ViewModel。
    /// </summary>
    public partial class AlarmViewModel : ObservableObject
    {
        private readonly IAlarmService _alarmService;
        private readonly IUserService _userService;
        private readonly ILogService _logService;

        [ObservableProperty]
        private int _activeCount;

        [ObservableProperty]
        private int _warningCount;

        [ObservableProperty]
        private int _heavyCount;

        [ObservableProperty]
        private int _fatalCount;

        [ObservableProperty]
        private int _selectedAlarmCode;

        /// <summary>活跃报警列表</summary>
        public ObservableCollection<AlarmRecord> ActiveAlarms { get; } = new();

        /// <summary>历史报警列表</summary>
        public ObservableCollection<AlarmRecord> AlarmHistory { get; } = new();

        public AlarmViewModel(IAlarmService alarmService, IUserService userService, ILogService logService)
        {
            _alarmService = alarmService;
            _userService = userService;
            _logService = logService;

            _alarmService.AlarmTriggered += (_, alarm) => RefreshAlarms();
            _alarmService.AlarmCleared += (_, _) => RefreshAlarms();
        }

        /// <summary>刷新报警数据</summary>
        [RelayCommand]
        private void RefreshAlarms()
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActiveAlarms.Clear();
                foreach (var a in _alarmService.ActiveAlarms)
                    ActiveAlarms.Add(a);

                ActiveCount = ActiveAlarms.Count;
                WarningCount = ActiveAlarms.Count(a => a.Level == AlarmLevel.Warning);
                HeavyCount = ActiveAlarms.Count(a => a.Level == AlarmLevel.Heavy);
                FatalCount = ActiveAlarms.Count(a => a.Level == AlarmLevel.Fatal);
            });
        }

        /// <summary>加载历史报警</summary>
        [RelayCommand]
        private async Task LoadHistory()
        {
            var history = await _alarmService.GetHistoryAsync(DateTime.Today.AddDays(-7), DateTime.Now);
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                AlarmHistory.Clear();
                foreach (var a in history.Take(200))
                    AlarmHistory.Add(a);
            });
        }

        /// <summary>确认并清除选中报警</summary>
        [RelayCommand]
        private void ClearSelected()
        {
            if (SelectedAlarmCode <= 0) return;
            var user = _userService.CurrentUser?.Username ?? "System";
            _alarmService.ClearAlarm(SelectedAlarmCode, user);
            _logService.Log(LogLevel.Info, "报警管理", $"报警 {SelectedAlarmCode} 已被 {user} 确认清除");
        }

        /// <summary>清除所有报警</summary>
        [RelayCommand]
        private void ClearAll()
        {
            var user = _userService.CurrentUser?.Username ?? "System";
            _alarmService.ClearAllAlarms(user);
            _logService.Log(LogLevel.Info, "报警管理", $"所有报警已被 {user} 清除");
        }

        /// <summary>触发测试报警（调试用）</summary>
        [RelayCommand]
        private void TriggerTestAlarm()
        {
            var code = 9000 + new Random().Next(1, 999);
            _alarmService.TriggerAlarm(code, AlarmLevel.Warning, "这是一条测试报警", "测试");
        }
    }
}
