using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TreeViewRenameDemo.Commands;
using TreeViewRenameDemo.Models;

namespace TreeViewRenameDemo.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// 功能：管理树形结构数据和重命名业务逻辑
    /// 设计思路：
    /// 1. 提供 TreeNodes 集合供 View 绑定
    /// 2. 实现重命名相关的三个命令：开始重命名、确认重命名、取消重命名
    /// 3. 包含输入验证逻辑
    /// 4. 初始化示例数据
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region 私有字段

        private TreeNode? _selectedNode;

        #endregion

        #region 公共属性

        /// <summary>
        /// 树节点根集合
        /// 绑定到 TreeView 的 ItemsSource
        /// </summary>
        public ObservableCollection<TreeNode> TreeNodes { get; set; }

        /// <summary>
        /// 当前选中的节点
        /// </summary>
        public TreeNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != value)
                {
                    _selectedNode = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 命令

        /// <summary>
        /// 重命名命令
        /// 功能：将选中节点设置为编辑模式
        /// </summary>
        public ICommand RenameCommand { get; }

        /// <summary>
        /// 确认重命名命令
        /// 功能：验证并保存新名称，退出编辑模式
        /// </summary>
        public ICommand ConfirmRenameCommand { get; }

        /// <summary>
        /// 取消重命名命令
        /// 功能：恢复原名称，退出编辑模式
        /// </summary>
        public ICommand CancelRenameCommand { get; }

        /// <summary>
        /// 工程载入命令
        /// 功能：载入工程项目
        /// </summary>
        public ICommand LoadProjectCommand { get; }

        /// <summary>
        /// 工程另存命令
        /// 功能：将工程另存为新文件
        /// </summary>
        public ICommand SaveProjectAsCommand { get; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// 初始化命令和数据
        /// </summary>
        public MainViewModel()
        {
            // 初始化命令
            RenameCommand = new RelayCommand(
                execute: StartRename,
                canExecute: CanStartRename
            );

            ConfirmRenameCommand = new RelayCommand(
                execute: ConfirmRename,
                canExecute: CanConfirmRename
            );

            CancelRenameCommand = new RelayCommand(
                execute: CancelRename,
                canExecute: parameter => parameter is TreeNode
            );

            // 工程载入命令
            LoadProjectCommand = new RelayCommand(
                execute: LoadProject,
                canExecute: parameter => parameter is TreeNode node && node.IsProjectNode
            );

            // 工程另存命令
            SaveProjectAsCommand = new RelayCommand(
                execute: SaveProjectAs,
                canExecute: parameter => parameter is TreeNode node && node.IsProjectNode
            );

            // 初始化树数据
            TreeNodes = new ObservableCollection<TreeNode>();
            InitializeTreeData();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化树形结构示例数据
        /// 数据结构：工程项目 → 控制器 → 系统总览、工艺参数配置、轴
        /// </summary>
        private void InitializeTreeData()
        {
            // 图标路径数据（Path Data 格式）
            string projectIcon = "M 2 2 L 14 2 L 14 14 L 2 14 Z";
            string controllerIcon = "M 3 3 L 13 3 L 13 13 L 3 13 Z M 6 6 L 10 6 M 6 9 L 10 9";
            string systemIcon = "M 8 2 L 14 8 L 8 14 L 2 8 Z";
            string paramIcon = "M 4 4 L 12 4 L 12 12 L 4 12 Z M 8 4 L 8 12 M 4 8 L 12 8";
            string axisIcon = "M 2 8 L 14 8 M 8 2 L 8 14 M 12 5 L 14 8 L 12 11";
            string ioIcon = "M 3 5 L 8 5 L 8 3 L 13 8 L 8 13 L 8 11 L 3 11 Z";

            // 创建根节点：工程项目
            var projectNode = new TreeNode
            {
                Name = "我的工程项目",
                OriginalName = "我的工程项目",
                Icon = projectIcon,
                IsExpanded = true,
                IsProjectNode = true,
                ToolTip = "工程项目根节点（右键可重命名、载入、另存）"
            };

            // 创建控制器节点
            var controllerNode = new TreeNode
            {
                Name = "控制器[192.168.1.100]",
                OriginalName = "控制器[192.168.1.100]",
                Icon = controllerIcon,
                IsExpanded = true,
                ToolTip = "控制器IP[192.168.1.100]"
            };

            // 创建系统总览节点
            var systemNode = new TreeNode
            {
                Name = "系统总览",
                OriginalName = "系统总览",
                Icon = systemIcon,
                ToolTip = "查看系统总览信息"
            };

            // 创建工艺参数配置节点
            var paramNode = new TreeNode
            {
                Name = "工艺参数配置",
                OriginalName = "工艺参数配置",
                Icon = paramIcon,
                ToolTip = "配置工艺参数"
            };

            // 创建轴节点
            var axisNode = new TreeNode
            {
                Name = "轴",
                OriginalName = "轴",
                Icon = axisIcon,
                IsExpanded = false,
                ToolTip = "轴配置"
            };

            // 创建 IO 轴子节点
            var ioAxisNode = new TreeNode
            {
                Name = "IO 轴",
                OriginalName = "IO 轴",
                Icon = ioIcon,
                ToolTip = "IO 轴配置"
            };

            // 构建树形结构
            axisNode.Children.Add(ioAxisNode);

            controllerNode.Children.Add(systemNode);
            controllerNode.Children.Add(paramNode);
            controllerNode.Children.Add(axisNode);

            projectNode.Children.Add(controllerNode);

            TreeNodes.Add(projectNode);
        }

        /// <summary>
        /// 判断是否可以开始重命名
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        /// <returns>true 表示可以开始重命名</returns>
        private bool CanStartRename(object? parameter)
        {
            // 只有工程项目节点才能重命名
            return parameter is TreeNode node && node.IsProjectNode && !node.IsEditing;
        }

        /// <summary>
        /// 开始重命名
        /// 执行逻辑：
        /// 1. 保存当前名称到 OriginalName（用于取消时恢复）
        /// 2. 设置 IsEditing = true（触发 UI 切换到 TextBox）
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        private void StartRename(object? parameter)
        {
            if (parameter is TreeNode node)
            {
                // 保存原始名称
                node.OriginalName = node.Name;

                // 进入编辑模式
                node.IsEditing = true;

                SelectedNode = node;
            }
        }

        /// <summary>
        /// 判断是否可以确认重命名
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        /// <returns>true 表示可以确认重命名</returns>
        private bool CanConfirmRename(object? parameter)
        {
            if (parameter is TreeNode node && node.IsEditing)
            {
                // 名称不能为空或纯空格
                return !string.IsNullOrWhiteSpace(node.Name);
            }
            return false;
        }

        /// <summary>
        /// 确认重命名
        /// 执行逻辑：
        /// 1. 验证输入有效性（非空、非纯空格）
        /// 2. Trim 去除首尾空格
        /// 3. 设置 IsEditing = false（退出编辑模式）
        /// 4. 显示成功消息
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        private void ConfirmRename(object? parameter)
        {
            if (parameter is TreeNode node)
            {
                // 验证输入
                if (string.IsNullOrWhiteSpace(node.Name))
                {
                    MessageBox.Show("节点名称不能为空！", "验证失败",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Trim 去除首尾空格
                node.Name = node.Name.Trim();

                // 退出编辑模式
                node.IsEditing = false;

                // ⭐ MessageBox 展示命令触发位置
                MessageBox.Show(
                    $"【确认重命名命令】已触发！\n\n" +
                    $"代码位置：MainViewModel.cs → ConfirmRename() 方法\n" +
                    $"原名称：{node.OriginalName}\n" +
                    $"新名称：{node.Name}",
                    "重命名成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 取消重命名
        /// 执行逻辑：
        /// 1. 恢复原始名称
        /// 2. 设置 IsEditing = false（退出编辑模式）
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        private void CancelRename(object? parameter)
        {
            if (parameter is TreeNode node)
            {
                // 恢复原始名称
                node.Name = node.OriginalName;

                // 退出编辑模式
                node.IsEditing = false;
            }
        }

        /// <summary>
        /// 工程载入
        /// 功能：载入已有工程项目
        /// 在这里编写你的载入逻辑
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        private void LoadProject(object? parameter)
        {
            if (parameter is TreeNode node)
            {
                // ⭐ MessageBox 展示命令触发位置
                MessageBox.Show(
                    $"【工程载入命令】已触发！\n\n" +
                    $"代码位置：MainViewModel.cs → LoadProject() 方法\n" +
                    $"当前工程：{node.Name}\n\n" +
                    $"请在此方法中编写你的工程载入业务逻辑。",
                    "工程载入",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 工程另存
        /// 功能：将当前工程另存为新文件
        /// 在这里编写你的另存逻辑
        /// </summary>
        /// <param name="parameter">命令参数（TreeNode 对象）</param>
        private void SaveProjectAs(object? parameter)
        {
            if (parameter is TreeNode node)
            {
                // ⭐ MessageBox 展示命令触发位置
                MessageBox.Show(
                    $"【工程另存命令】已触发！\n\n" +
                    $"代码位置：MainViewModel.cs → SaveProjectAs() 方法\n" +
                    $"当前工程：{node.Name}\n\n" +
                    $"请在此方法中编写你的工程另存业务逻辑。",
                    "工程另存",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region INotifyPropertyChanged 实现

        /// <summary>
        /// 属性变更事件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">属性名称</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
