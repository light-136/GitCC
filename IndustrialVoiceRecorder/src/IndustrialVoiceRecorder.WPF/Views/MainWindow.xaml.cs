using IndustrialVoiceRecorder.Domain.Entities;
using IndustrialVoiceRecorder.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace IndustrialVoiceRecorder.WPF.Views;

/// <summary>
/// 主窗口代码 (v3.1)
/// 复核模式控制：非复核模式下取消编辑
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
            DataContext = _viewModel;
            await _viewModel.InitializeCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// DataGrid开始编辑时检查是否处于复核模式
    /// </summary>
    private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (_viewModel == null || !_viewModel.IsReviewMode)
        {
            e.Cancel = true; // 非复核模式，取消编辑
        }
    }

    /// <summary>
    /// 单元格编辑结束，保存修改
    /// </summary>
    private async void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (_viewModel == null) return;

        var record = e.Row.Item as VoiceRecord;
        if (record == null) return;

        string columnHeader = e.Column.Header?.ToString() ?? "";
        string newValue = "";

        if (e.EditingElement is TextBox textBox)
            newValue = textBox.Text;
        else if (e.EditingElement is ComboBox comboBox)
            newValue = comboBox.SelectedItem?.ToString() ?? "";

        if (string.IsNullOrEmpty(newValue)) return;

        var success = await _viewModel.OnCellEditEndingAsync(record, columnHeader, newValue);
        if (!success)
        {
            e.Cancel = true;
        }
    }
}
