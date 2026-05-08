using System;
using System.Windows;

namespace SmartMES.UI.Services
{
    /// <summary>
    /// 主题管理器 — 在运行时切换暗色/浅色主题。
    /// 原理：替换 Application.Resources 中合并的资源字典，
    /// 使所有通过 DynamicResource 引用颜色 Brush 的控件自动刷新。
    /// </summary>
    public static class ThemeManager
    {
        // 浅色主题资源字典的 Pack URI
        private static readonly Uri LightThemeUri =
            new Uri("Resources/LightTheme.xaml", UriKind.Relative);

        // 暗色主题（默认）资源字典的 Pack URI
        private static readonly Uri DarkThemeUri =
            new Uri("Resources/Styles.xaml", UriKind.Relative);

        // 当前是否处于浅色主题
        private static bool _isLightTheme;
        public static bool IsLightTheme => _isLightTheme;

        /// <summary>
        /// 切换主题 — 暗色 ↔ 浅色。
        /// 将浅色主题字典追加到或移除出 Application.Resources.MergedDictionaries。
        /// 浅色字典中的同名 Key 会覆盖暗色字典的值（后加载优先）。
        /// </summary>
        public static void ToggleTheme()
        {
            var mergedDicts = Application.Current.Resources.MergedDictionaries;

            if (_isLightTheme)
            {
                // 当前是浅色 → 切回暗色：移除浅色主题字典
                ResourceDictionary? toRemove = null;
                foreach (var dict in mergedDicts)
                {
                    if (dict.Source != null && dict.Source.OriginalString.Contains("LightTheme"))
                    {
                        toRemove = dict;
                        break;
                    }
                }
                if (toRemove != null)
                    mergedDicts.Remove(toRemove);

                _isLightTheme = false;
            }
            else
            {
                // 当前是暗色 → 切到浅色：追加浅色主题字典
                var lightDict = new ResourceDictionary { Source = LightThemeUri };
                mergedDicts.Add(lightDict);
                _isLightTheme = true;
            }
        }
    }
}
