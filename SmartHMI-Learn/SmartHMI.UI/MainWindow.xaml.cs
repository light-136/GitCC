using SmartHMI.UI.ViewModels;
using System.Windows;

namespace SmartHMI.UI;

/// <summary>
/// MainWindow 代码后置文件
/// MVVM 说明：
///   此文件只做一件事：通过构造函数接收 MainViewModel（DI 注入），
///   并设置为 DataContext。
///   所有导航逻辑、业务逻辑均在 MainViewModel 中处理，
///   View 层保持纯净，不包含任何业务代码。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造函数 — 通过 DI 注入 MainViewModel
    /// </summary>
    /// <param name="vm">主窗口 ViewModel（由 DI 容器提供）</param>
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        // 设置数据上下文，所有绑定从此 ViewModel 解析
        DataContext = vm;
    }
}
