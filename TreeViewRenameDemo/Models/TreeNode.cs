using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TreeViewRenameDemo.Models
{
    /// <summary>
    /// 树节点数据模型
    /// 功能：表示树形结构中的一个节点，支持属性变更通知
    /// 设计思路：
    /// 1. 实现 INotifyPropertyChanged 接口以支持 MVVM 数据绑定
    /// 2. 使用 ObservableCollection 存储子节点，自动通知集合变化
    /// 3. 新增 IsEditing 属性支持重命名功能
    /// 4. 保存原始名称 OriginalName 用于取消重命名时恢复
    /// </summary>
    public class TreeNode : INotifyPropertyChanged
    {
        #region 私有字段

        private string _name = string.Empty;
        private string _originalName = string.Empty;
        private string _icon = string.Empty;
        private string _toolTip = string.Empty;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isEditing;
        private bool _isProjectNode;

        #endregion

        #region 公共属性

        /// <summary>
        /// 节点显示名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 原始名称（用于取消重命名时恢复）
        /// </summary>
        public string OriginalName
        {
            get => _originalName;
            set
            {
                if (_originalName != value)
                {
                    _originalName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 节点图标（Path Data 格式）
        /// 例如："M 0 0 L 10 10 L 0 20 Z"
        /// </summary>
        public string Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 工具提示文本
        /// </summary>
        public string ToolTip
        {
            get => _toolTip;
            set
            {
                if (_toolTip != value)
                {
                    _toolTip = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否展开子节点
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否被选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否处于编辑状态（重命名模式）
        /// 当为 true 时，节点显示为 TextBox 可编辑
        /// 当为 false 时，节点显示为 TextBlock 只读
        /// </summary>
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否为工程项目节点（只有工程项目节点才能重命名）
        /// </summary>
        public bool IsProjectNode
        {
            get => _isProjectNode;
            set
            {
                if (_isProjectNode != value)
                {
                    _isProjectNode = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 子节点集合
        /// 使用 ObservableCollection 自动通知集合变化
        /// </summary>
        public ObservableCollection<TreeNode> Children { get; set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// 初始化子节点集合
        /// </summary>
        public TreeNode()
        {
            Children = new ObservableCollection<TreeNode>();
        }

        #endregion

        #region INotifyPropertyChanged 实现

        /// <summary>
        /// 属性变更事件
        /// 当属性值改变时通知绑定的 UI 元素更新
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">属性名称（自动获取）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
