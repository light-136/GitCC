// 日志颜色辅助转换器
// 根据日志内容中的关键字返回分类标识，用于XAML中的DataTrigger颜色高亮

using System.Globalization;
using System.Windows.Data;

namespace GD32FirmwareUpdater.Converters
{
    /// <summary>
    /// 日志行分类转换器
    /// 根据日志内容返回分类字符串，配合DataTrigger实现语法高亮
    /// </summary>
    public class LogColorHelper : IValueConverter
    {
        // 单例，供XAML中 x:Static 引用
        public static readonly LogColorHelper Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                if (text.Contains("[TX]"))
                    return "TX";
                if (text.Contains("[RX]"))
                    return "RX";
                if (text.Contains("失败") || text.Contains("异常") || text.Contains("错误") || text.Contains("超时"))
                    return "ERROR";
                if (text.Contains("成功") || text.Contains("完成"))
                    return "SUCCESS";
            }
            return "NORMAL";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
