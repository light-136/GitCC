// 文件：NavigationService.cs
// 层级：表现层（UI）> Services
// 职责：页面导航服务，管理当前显示的页面切换

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace SmartIndustry.UI.Services
{
    /// <summary>
    /// 导航服务 — 管理主内容区域的页面切换
    /// </summary>
    public class NavigationService : INotifyPropertyChanged
    {
        private static NavigationService? _instance;
        public static NavigationService Instance => _instance ??= new NavigationService();

        private UserControl? _currentPage;
        private string _currentPageName = "仪表盘";

        /// <summary>当前显示的页面</summary>
        public UserControl? CurrentPage
        {
            get => _currentPage;
            set { _currentPage = value; OnPropertyChanged(); }
        }

        /// <summary>当前页面名称</summary>
        public string CurrentPageName
        {
            get => _currentPageName;
            set { _currentPageName = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 导航到指定页面
        /// </summary>
        public void NavigateTo(string pageName)
        {
            CurrentPageName = pageName;
            CurrentPage = PageFactory.CreatePage(pageName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
