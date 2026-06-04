// 布尔值到颜色的转换器
// 用于串口连接状态指示灯的颜色绑定

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GD32FirmwareUpdater.Converters
{
    /// <summary>
    /// 布尔值转颜色转换器
    /// true → 绿色（已连接），false → 灰色（未连接）
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
                return new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)); // 绿色
            return new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));     // 灰色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值取反转换器
    /// 用于按钮的启用/禁用状态控制
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return value;
        }
    }
}
