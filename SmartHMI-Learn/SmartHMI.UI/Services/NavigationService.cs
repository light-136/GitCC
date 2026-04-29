using Microsoft.Extensions.DependencyInjection;

namespace SmartHMI.UI.Services;

/// <summary>
/// 导航服务实现
/// 技术思路：
///   1. 持有 IServiceProvider，通过 DI 容器解析目标 ViewModel 实例
///   2. 切换 CurrentViewModel 属性，触发 CurrentViewModelChanged 事件
///   3. MainViewModel 订阅此事件，更新自身 CurrentViewModel 属性
///   4. MainWindow 的 ContentControl 绑定 MainViewModel.CurrentViewModel
///   5. WPF 根据 App.xaml 中的 DataTemplate 自动渲染对应 View
/// </summary>
public class NavigationService : INavigationService
{
    // DI 容器，用于解析 ViewModel 实例
    private readonly IServiceProvider _serviceProvider;

    /// <summary>当前显示的 ViewModel 实例</summary>
    public object? CurrentViewModel { get; private set; }

    /// <summary>ViewModel 切换事件</summary>
    public event EventHandler? CurrentViewModelChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="serviceProvider">DI 服务容器</param>
    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 导航到指定 ViewModel 对应的页面
    /// 从 DI 容器中解析 TViewModel 实例，设置为当前 ViewModel
    /// </summary>
    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        // 从 DI 容器解析 ViewModel（单例模式，每次返回同一实例）
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        CurrentViewModel = viewModel;
        // 通知订阅者（MainViewModel）当前 ViewModel 已变化
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
