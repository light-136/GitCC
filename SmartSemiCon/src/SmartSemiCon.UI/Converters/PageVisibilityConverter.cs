// ============================================================
// 文件：PageVisibilityConverter.cs
// 用途：页面导航可见性转换器
// 设计思路：
//   MainWindow中通过CurrentPage属性控制哪个页面可见。
//   转换器比较CurrentPage枚举值与ConverterParameter，
//   匹配时返回Visible，否则返回Collapsed。
//   这样每个页面Grid只在被选中时显示，实现无框架导航。
// ============================================================

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SmartSemiCon.UI.ViewModels;

namespace SmartSemiCon.UI.Converters
{
    /// <summary>
    /// 页面可见性转换器 — 当CurrentPage与参数匹配时返回Visible。
    /// </summary>
    public class PageVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NavigationPage currentPage && parameter is string pageStr)
            {
                if (Enum.TryParse<NavigationPage>(pageStr, out var targetPage))
                {
                    return currentPage == targetPage ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
