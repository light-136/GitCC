using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TreeViewRenameDemo.Models;
using TreeViewRenameDemo.ViewModels;

namespace TreeViewRenameDemo.Views
{
    /// <summary>
    /// MainWindow 代码隐藏
    /// 功能：处理无法在纯 MVVM 中完成的 UI 交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        /// <summary>
        /// TreeView 右键菜单打开事件
        /// 核心逻辑：只有 IsProjectNode == true 的节点才显示右键菜单，其余节点直接取消
        /// </summary>
        private void TreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 从鼠标点击位置向上查找 TreeViewItem
            if (e.OriginalSource is not DependencyObject source) { e.Handled = true; return; }

            var treeViewItem = FindParent<TreeViewItem>(source);
            if (treeViewItem == null) { e.Handled = true; return; }

            // 获取节点数据
            if (treeViewItem.DataContext is TreeNode node && node.IsProjectNode)
            {
                // 工程项目节点：从资源中取出菜单并赋值
                treeViewItem.ContextMenu = (ContextMenu)FindResource("ProjectContextMenu");
                treeViewItem.ContextMenu.DataContext = node;
            }
            else
            {
                // 非工程项目节点：阻止弹出任何菜单
                e.Handled = true;
            }
        }

        /// <summary>
        /// 向上查找指定类型的父元素
        /// </summary>
        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T target) return target;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not TreeNode node) return;

            switch (e.Key)
            {
                case Key.Enter:
                    ViewModel?.ConfirmRenameCommand.Execute(node);
                    e.Handled = true;
                    LeftTreeList.Focus();
                    break;
                case Key.Escape:
                    ViewModel?.CancelRenameCommand.Execute(node);
                    e.Handled = true;
                    LeftTreeList.Focus();
                    break;
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not TreeNode node) return;
            if (node.IsEditing)
            {
                ViewModel?.ConfirmRenameCommand.Execute(node);
            }
        }

        private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not TreeNode node) return;
            if (node.IsEditing)
            {
                textBox.Dispatcher.InvokeAsync(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }, DispatcherPriority.Input);
            }
        }
    }
}
