// 文件：MainWindow.xaml.cs
// 层级：表现层（UI）> Views
// 职责：主窗口代码隐藏，处理导航按钮点击事件

using System.Windows;
using System.Windows.Controls;
using SmartIndustry.UI.Services;

namespace SmartIndustry.UI.Views
{
    /// <summary>
    /// 主窗口 — 包含左侧导航栏和右侧内容区域
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // 默认显示仪表盘页面
            NavigateTo("仪表盘");
        }

        /// <summary>
        /// 导航按钮点击事件 — 根据按钮 Tag 切换页面
        /// </summary>
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pageName)
            {
                NavigateTo(pageName);
            }
        }

        /// <summary>
        /// 切换到指定页面
        /// </summary>
        private void NavigateTo(string pageName)
        {
            contentArea.Content = PageFactory.CreatePage(pageName);
            txtCurrentPage.Text = pageName;
        }
    }
}
