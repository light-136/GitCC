using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Domain.Entities;
using IndustrialVoiceRecorder.Infrastructure.Persistence;
using IndustrialVoiceRecorder.Infrastructure.Services;
using IndustrialVoiceRecorder.Shared;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace IndustrialVoiceRecorder.WPF.ViewModels;

/// <summary>
/// 主界面ViewModel (v3.1)
/// 修复：编辑后立即刷新显示、复核改为登录/登出模式
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly VoiceProcessingService _voiceProcessingService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IRecordService _recordService;
    private readonly IExportService _exportService;
    private readonly IVoiceRecordRepository _repository;
    private readonly ILogger<MainViewModel> _logger;

    private const string LastOperatorKey = "LastOperator";
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioReader;

    // ==================== 属性 ====================
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _currentDeviceName = "未连接";
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private string _realtimeText = string.Empty;
    [ObservableProperty] private bool _isEngineLoaded;
    [ObservableProperty] private string _loadingText = "正在加载...";
    [ObservableProperty] private int _todayTotal;
    [ObservableProperty] private int _todayOkCount;
    [ObservableProperty] private int _todayNgCount;
    [ObservableProperty] private string _todayPassRate = "0%";
    [ObservableProperty] private DateTime _filterDate = DateTime.Today;
    [ObservableProperty] private VoiceRecord? _selectedRecord;

    // ==================== 复核登录状态 ====================

    /// <summary>
    /// 当前是否处于复核模式（复核人已登录）
    /// </summary>
    [ObservableProperty] private bool _isReviewMode;

    /// <summary>
    /// 当前复核人名称（登录后显示）
    /// </summary>
    [ObservableProperty] private string _currentReviewer = "";

    /// <summary>
    /// 复核模式按钮文本
    /// </summary>
    public string ReviewButtonText => IsReviewMode ? $"复核登出 ({CurrentReviewer})" : "复核登录";

    // ==================== 集合 ====================
    public ObservableCollection<OperatorInfo> OperatorList { get; } = new();
    [ObservableProperty] private OperatorInfo? _selectedOperator;
    public ObservableCollection<string> StatusOptions { get; } = new() { "OK", "NG" };
    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    public ObservableCollection<VoiceRecord> Records { get; } = new();

    public MainViewModel(
        VoiceProcessingService voiceProcessingService,
        IAudioCaptureService audioCaptureService,
        IRecordService recordService,
        IExportService exportService,
        IVoiceRecordRepository repository,
        ILogger<MainViewModel> logger)
    {
        _voiceProcessingService = voiceProcessingService;
        _audioCaptureService = audioCaptureService;
        _recordService = recordService;
        _exportService = exportService;
        _repository = repository;
        _logger = logger;

        _voiceProcessingService.RecordSaved += OnRecordSaved;
        _voiceProcessingService.RealtimeTextReceived += OnRealtimeTextReceived;
        _voiceProcessingService.StatusChanged += OnStatusChanged;
    }

    // 通知ReviewButtonText更新
    partial void OnIsReviewModeChanged(bool value) => OnPropertyChanged(nameof(ReviewButtonText));
    partial void OnCurrentReviewerChanged(string value) => OnPropertyChanged(nameof(ReviewButtonText));

    // ==================== 初始化 ====================

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            RefreshDevices();
            await LoadOperatorsAsync();

            try
            {
                LoadingText = "正在连接FunASR...";
                await _voiceProcessingService.InitializeAsync("funasr");
                IsEngineLoaded = true;
                StatusText = "FunASR已就绪";
            }
            catch { StatusText = "⚠ FunASR未启动"; }

            await LoadRecordsByDateAsync();
        }
        catch (Exception ex) { StatusText = $"初始化失败: {ex.Message}"; }
    }

    // ==================== 操作员 ====================

    private async Task LoadOperatorsAsync()
    {
        var operators = await _repository.GetOperatorsAsync();
        OperatorList.Clear();
        foreach (var op in operators) OperatorList.Add(op);
        if (OperatorList.Count == 0)
        {
            var d = new OperatorInfo { Name = "默认操作员", WorkId = "001" };
            d.Id = await _repository.AddOperatorAsync(d);
            OperatorList.Add(d);
        }
        var last = await _repository.GetSettingAsync(LastOperatorKey);
        SelectedOperator = (!string.IsNullOrEmpty(last)
            ? OperatorList.FirstOrDefault(o => o.Name == last) : null) ?? OperatorList[0];
    }

    partial void OnSelectedOperatorChanged(OperatorInfo? value)
    {
        if (value != null)
        {
            _voiceProcessingService.CurrentOperator = value.DisplayName;
            _ = _repository.SaveSettingAsync(LastOperatorKey, value.Name);
        }
    }

    [RelayCommand]
    private async Task ManageOperatorsAsync()
    {
        var win = new Window
        {
            Title = "操作员管理", Width = 400, Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = System.Windows.Application.Current.MainWindow
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "操作员管理", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 15) });
        var listInfo = new TextBlock
        {
            Text = $"当前: {string.Join("、", OperatorList.Select(o => o.DisplayName))}",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 15), FontSize = 12
        };
        panel.Children.Add(listInfo);
        panel.Children.Add(new TextBlock { Text = "姓名:", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Height = 28, FontSize = 13, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "工号(可选):", FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
        var workIdBox = new TextBox { Height = 28, FontSize = 13, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(workIdBox);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var addBtn = new Button { Content = "添加", Width = 80, Height = 30 };
        var closeBtn = new Button { Content = "关闭", Width = 80, Height = 30, Margin = new Thickness(10, 0, 0, 0) };
        addBtn.Click += async (s, e) =>
        {
            var name = nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("请输入姓名！"); return; }
            if (OperatorList.Any(o => o.Name == name)) { MessageBox.Show("已存在！"); return; }
            var op = new OperatorInfo { Name = name, WorkId = workIdBox.Text?.Trim() };
            op.Id = await _repository.AddOperatorAsync(op);
            OperatorList.Add(op); SelectedOperator = op;
            nameBox.Clear(); workIdBox.Clear();
            listInfo.Text = $"当前: {string.Join("、", OperatorList.Select(o => o.DisplayName))}";
        };
        closeBtn.Click += (s, e) => win.Close();
        btnPanel.Children.Add(addBtn); btnPanel.Children.Add(closeBtn);
        panel.Children.Add(btnPanel);
        win.Content = panel; win.ShowDialog();
    }

    // ==================== 复核登录/登出 ====================

    /// <summary>
    /// 复核登录/登出切换
    /// 登录：弹窗选择复核人 → 进入复核模式（列表可编辑）
    /// 登出：退出复核模式（列表变只读）
    /// </summary>
    [RelayCommand]
    private void ToggleReviewMode()
    {
        if (IsReviewMode)
        {
            // 登出
            IsReviewMode = false;
            CurrentReviewer = "";
            StatusText = "复核模式已退出";
            _logger.LogInformation("复核人已登出");
        }
        else
        {
            // 弹窗登录
            var win = new Window
            {
                Title = "复核人登录", Width = 350, Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = System.Windows.Application.Current.MainWindow
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "请选择复核人", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
            var combo = new ComboBox
            {
                Width = 200, Height = 30, FontSize = 13,
                ItemsSource = OperatorList.Select(o => o.DisplayName).ToList(),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(combo);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "登录", Width = 80, Height = 30 };
            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(10, 0, 0, 0) };
            okBtn.Click += (s, e) =>
            {
                if (combo.SelectedItem == null) { MessageBox.Show("请选择复核人！"); return; }
                CurrentReviewer = combo.SelectedItem.ToString()!;
                IsReviewMode = true;
                StatusText = $"复核模式：{CurrentReviewer}（可直接编辑列表）";
                _logger.LogInformation("复核人登录: {Reviewer}", CurrentReviewer);
                win.DialogResult = true; win.Close();
            };
            cancelBtn.Click += (s, e) => { win.DialogResult = false; win.Close(); };
            bp.Children.Add(okBtn); bp.Children.Add(cancelBtn);
            panel.Children.Add(bp);
            win.Content = panel; win.ShowDialog();
        }
    }

    // ==================== 录音控制 ====================

    [RelayCommand]
    private void RefreshDevices()
    {
        AudioDevices.Clear();
        var devices = _audioCaptureService.GetAvailableDevices();
        foreach (var d in devices) AudioDevices.Add(d);
        if (AudioDevices.Count > 0)
        {
            SelectedDevice = AudioDevices.FirstOrDefault(d => d.IsBluetooth) ?? AudioDevices[0];
            IsDeviceConnected = true; CurrentDeviceName = SelectedDevice.Name;
        }
        else { IsDeviceConnected = false; CurrentDeviceName = "无设备"; }
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        try
        {
            if (IsRecording)
            {
                _voiceProcessingService.Stop(); IsRecording = false; StatusText = "已停止录音";
            }
            else
            {
                if (!IsEngineLoaded) { MessageBox.Show("识别引擎未加载！"); return; }
                if (SelectedDevice == null) { MessageBox.Show("请选择音频设备！"); return; }
                if (SelectedOperator == null) { MessageBox.Show("请选择操作员！"); return; }
                _audioCaptureService.SelectDevice(SelectedDevice.Index);
                _voiceProcessingService.CurrentOperator = SelectedOperator.DisplayName;
                _voiceProcessingService.Start(); IsRecording = true; StatusText = "正在录音...";
            }
        }
        catch (Exception ex) { IsRecording = false; MessageBox.Show($"操作失败: {ex.Message}"); }
    }

    // ==================== 音频播放 ====================

    [RelayCommand]
    private void PlayAudio(VoiceRecord? record)
    {
        if (record == null) record = SelectedRecord;
        if (record?.AudioFilePath == null || !File.Exists(record.AudioFilePath))
        { MessageBox.Show("音频文件不存在！"); return; }
        try
        {
            StopAudio();
            _audioReader = new AudioFileReader(record.AudioFilePath);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioReader);
            _waveOut.PlaybackStopped += (s, e) => { _audioReader?.Dispose(); _waveOut?.Dispose(); _audioReader = null; _waveOut = null; };
            _waveOut.Play();
            StatusText = $"▶ 播放: {record.SerialNumber}";
        }
        catch (Exception ex) { MessageBox.Show($"播放失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void StopAudio()
    {
        _waveOut?.Stop(); _audioReader?.Dispose(); _waveOut?.Dispose(); _audioReader = null; _waveOut = null;
    }

    // ==================== 列表编辑保存 ====================

    /// <summary>
    /// 单元格编辑结束时调用
    /// 复核模式下直接保存，非复核模式下拒绝编辑
    /// </summary>
    public async Task<bool> OnCellEditEndingAsync(VoiceRecord record, string columnHeader, string newValue)
    {
        if (record == null) return false;

        // 非复核模式不允许编辑
        if (!IsReviewMode)
        {
            MessageBox.Show("请先点击「复核登录」进入复核模式！", "提示");
            return false; // 告诉调用方取消编辑
        }

        try
        {
            string fieldName = "";
            string oldValue = "";

            switch (columnHeader)
            {
                case "数字化文本":
                    oldValue = record.NormalizedText ?? "";
                    record.NormalizedText = newValue;  // 立即更新对象属性
                    record.CorrectedText = newValue;
                    fieldName = "NormalizedText";
                    break;
                case "状态":
                    oldValue = record.Status ?? "";
                    record.Status = newValue;
                    fieldName = "Status";
                    break;
                case "备注":
                    oldValue = record.Remarks ?? "";
                    record.Remarks = newValue;
                    fieldName = "Remarks";
                    break;
                default:
                    return true;
            }

            record.CorrectedBy = CurrentReviewer;
            record.CorrectedAt = DateTime.Now;
            record.IsReviewed = true;

            await _repository.UpdateAsync(record);

            // 记录修改历史
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.DatabaseDirectory, AppConfig.DatabaseFileName)}");
            await conn.OpenAsync();
            await Dapper.SqlMapper.ExecuteAsync(conn,
                @"INSERT INTO EditHistory (RecordId, FieldName, OldValue, NewValue, EditedBy)
                  VALUES (@RecordId, @Field, @Old, @New, @By)",
                new { RecordId = record.Id, Field = fieldName, Old = oldValue, New = newValue, By = CurrentReviewer });

            StatusText = $"✅ {record.SerialNumber} 已修改";
            UpdateStatistics();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存编辑失败");
            MessageBox.Show($"保存失败: {ex.Message}");
            return false;
        }
    }

    // ==================== 日期筛选 ====================

    partial void OnFilterDateChanged(DateTime value) => _ = LoadRecordsByDateAsync();

    [RelayCommand]
    private async Task LoadRecordsByDateAsync()
    {
        try
        {
            var start = FilterDate.Date;
            var records = await _recordService.GetRecordsAsync(start, start.AddDays(1));
            Records.Clear();
            foreach (var r in records) Records.Add(r);
            UpdateStatistics();
        }
        catch (Exception ex) { _logger.LogError(ex, "加载记录失败"); }
    }

    // ==================== 导出 ====================

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (Records.Count == 0) { MessageBox.Show("没有可导出的记录！"); return; }
        try
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.ExportDirectory);
            var file = Path.Combine(dir, $"语音记录_{FilterDate:yyyyMMdd}_{SelectedOperator?.Name ?? "all"}.xlsx");
            StatusText = "正在导出...";
            await _exportService.ExportToExcelAsync(Records.ToList(), file);
            StatusText = "导出完成";
            MessageBox.Show($"导出成功！\n{file}");
        }
        catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void ClearRecords()
    {
        if (MessageBox.Show("确定清空显示列表？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { Records.Clear(); UpdateStatistics(); }
    }

    // ==================== 事件 ====================

    private void OnRecordSaved(object? sender, VoiceRecord record)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (FilterDate.Date == DateTime.Today) { Records.Insert(0, record); UpdateStatistics(); }
        });
    }

    private void OnRealtimeTextReceived(object? sender, string text)
        => System.Windows.Application.Current.Dispatcher.Invoke(() => RealtimeText = text);

    private void OnStatusChanged(object? sender, string status)
        => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = status);

    private void UpdateStatistics()
    {
        TodayTotal = Records.Count;
        TodayOkCount = Records.Count(r => r.Status == "OK");
        TodayNgCount = Records.Count(r => r.Status == "NG");
        TodayPassRate = TodayTotal > 0 ? $"{(double)TodayOkCount / TodayTotal * 100:F1}%" : "0%";
    }
}
