namespace SmartHMI.UI.Services;

/// <summary>
/// 导航服务接口 — MVVM 导航抽象
/// 作用：让 ViewModel 层可以触发页面导航，而不依赖任何 View 类型。
/// ViewModel 只需调用 NavigateTo&lt;T&gt;()，不需要知道 View 的存在。
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 导航到指定 ViewModel 对应的页面
    /// </summary>
    /// <typeparam name="TViewModel">目标 ViewModel 类型</typeparam>
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>
    /// 当前显示的 ViewModel（用于 MainWindow 绑定）
    /// </summary>
    object? CurrentViewModel { get; }

    /// <summary>
    /// 当前 ViewModel 变化时触发（MainViewModel 订阅此事件刷新 UI）
    /// </summary>
    event EventHandler? CurrentViewModelChanged;
}
