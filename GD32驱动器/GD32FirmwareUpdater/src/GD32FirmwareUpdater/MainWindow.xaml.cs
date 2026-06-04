using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace GD32FirmwareUpdater;

/// <summary>
/// 主窗口代码后置
/// 处理日志自动滚动等UI交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 监听日志集合变化，自动滚动到最新条目
        if (LogListBox.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (s, args) =>
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            };
        }
    }
}
