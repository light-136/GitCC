using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TreeViewRenameDemo.Converters
{
    /// <summary>
    /// 布尔值转可见性转换器
    /// 功能：将 bool 值转换为 Visibility 枚举
    /// 使用场景：控制编辑模式下 TextBlock 和 TextBox 的显示切换
    /// 转换规则：true → Visible, false → Collapsed
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 正向转换：bool → Visibility
        /// </summary>
        /// <param name="value">输入的 bool 值</param>
        /// <param name="targetType">目标类型（Visibility）</param>
        /// <param name="parameter">转换参数（未使用）</param>
        /// <param name="culture">区域信息（未使用）</param>
        /// <returns>Visibility.Visible 或 Visibility.Collapsed</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        /// <summary>
        /// 反向转换：Visibility → bool
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// 反向布尔值转可见性转换器
    /// 功能：将 bool 值转换为 Visibility 枚举（取反）
    /// 转换规则：true → Collapsed, false → Visible
    /// 使用场景：与 BoolToVisibilityConverter 配合使用，实现互斥显示
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 正向转换：bool → Visibility（取反）
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        /// <summary>
        /// 反向转换：Visibility → bool（取反）
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return true;
        }
    }
}
