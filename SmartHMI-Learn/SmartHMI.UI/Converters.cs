using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartHMI.UI;

/// <summary>
/// 转换器静态工厂类（供 XAML 中 {x:Static} 引用使用）
/// </summary>
public static class Converters
{
    public static readonly IValueConverter PositiveToVisible = new PositiveToVisibilityConverter();
    public static readonly IValueConverter BoolToOnOff = new BoolToOnOffConverter();
    public static readonly IValueConverter StatusToColor = new StatusToColorConverter();
    public static readonly IValueConverter BoolToColor = new BoolToColorConverter();
    public static readonly IValueConverter BoolToVisible = new BoolToVisibilityConverter();
}

public class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToOnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "ON" : "OFF";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var colorStr = value?.ToString() switch
        {
            "Online" or "Connected" => "#00FF88",
            "Offline" or "Disconnected" => "#888888",
            "Faulted" => "#FF4444",
            "Connecting" or "Reconnecting" => "#FFB800",
            _ => "#888888"
        };
        return System.Windows.Media.ColorConverter.ConvertFromString(colorStr) is System.Windows.Media.Color c ? c : System.Windows.Media.Colors.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var colorStr = value is bool b && b ? "#0A4A2A" : "#2D2D4E";
        return System.Windows.Media.ColorConverter.ConvertFromString(colorStr) is System.Windows.Media.Color c ? c : System.Windows.Media.Colors.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool → Visibility 转换器
/// true → Visible，false → Collapsed
/// 用于：登录后显示导航栏、退出按钮等
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
