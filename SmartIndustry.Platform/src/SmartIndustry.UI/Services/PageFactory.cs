// 文件：PageFactory.cs
// 层级：表现层（UI）> Services
// 职责：页面工厂，根据名称创建对应的 UserControl 页面

using System.Windows.Controls;
using SmartIndustry.UI.Views.Dashboard;
using SmartIndustry.UI.Views.Motion;
using SmartIndustry.UI.Views.Vision;
using SmartIndustry.UI.Views.Communication;
using SmartIndustry.UI.Views.Recipe;
using SmartIndustry.UI.Views.Alarm;
using SmartIndustry.UI.Views.Settings;
using SmartIndustry.UI.Views.Log;

namespace SmartIndustry.UI.Services
{
    /// <summary>
    /// 页面工厂 — 根据导航名称创建对应页面实例
    /// </summary>
    public static class PageFactory
    {
        public static UserControl CreatePage(string pageName)
        {
            return pageName switch
            {
                "仪表盘" => new DashboardPage(),
                "运动控制" => new MotionPage(),
                "视觉检测" => new VisionPage(),
                "IO监控" => new CommunicationPage(),
                "通信管理" => new CommunicationPage(),
                "配方管理" => new RecipePage(),
                "报警日志" => new AlarmPage(),
                "日志管理" => new LogPage(),
                "系统设置" => new SettingsPage(),
                _ => new DashboardPage()
            };
        }
    }
}
