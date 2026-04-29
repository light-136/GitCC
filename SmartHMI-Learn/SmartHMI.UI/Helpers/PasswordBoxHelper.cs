using System.Windows;
using System.Windows.Controls;

namespace SmartHMI.UI.Helpers;

/// <summary>
/// PasswordBox MVVM 绑定辅助类
/// 技术背景：
///   WPF 的 PasswordBox.Password 属性出于安全考虑，不是依赖属性（DependencyProperty），
///   因此无法直接用 {Binding} 绑定。
/// 解决方案：
///   使用附加属性（Attached Property）模式，监听 PasswordBox 的 PasswordChanged 事件，
///   将密码同步到附加属性，再由附加属性绑定到 ViewModel 的 Password 属性。
/// 用法（XAML）：
///   helpers:PasswordBoxHelper.BoundPassword="{Binding Password, Mode=TwoWay}"
/// </summary>
public static class PasswordBoxHelper
{
    // 标记是否正在更新，防止循环触发
    private static bool _isUpdating;

    /// <summary>
    /// 附加属性：BoundPassword — 绑定到 ViewModel 的密码字符串
    /// </summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

    /// <summary>
    /// 附加属性：BindPassword — 是否启用绑定（需设为 True 才激活监听）
    /// </summary>
    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    // --- Getter / Setter ---

    public static string GetBoundPassword(DependencyObject d) =>
        (string)d.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject d, string value) =>
        d.SetValue(BoundPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject d) =>
        (bool)d.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject d, bool value) =>
        d.SetValue(BindPasswordProperty, value);

    // --- 事件处理 ---

    /// <summary>
    /// BindPassword 属性变化时：注册/注销 PasswordChanged 事件监听
    /// </summary>
    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if ((bool)e.OldValue) box.PasswordChanged -= HandlePasswordChanged;
        if ((bool)e.NewValue) box.PasswordChanged += HandlePasswordChanged;
    }

    /// <summary>
    /// BoundPassword 属性变化时（ViewModel → View 方向）：同步到 PasswordBox
    /// </summary>
    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (_isUpdating) return; // 防止循环
        box.Password = (string)e.NewValue ?? string.Empty;
    }

    /// <summary>
    /// PasswordBox 内容变化时（View → ViewModel 方向）：同步到 BoundPassword 附加属性
    /// </summary>
    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        _isUpdating = true;
        SetBoundPassword(box, box.Password);
        _isUpdating = false;
    }
}
