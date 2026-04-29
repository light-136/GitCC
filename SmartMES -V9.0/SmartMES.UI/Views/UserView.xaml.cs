using System.Windows.Controls;

namespace SmartMES.UI.Views
{
    public partial class UserView : UserControl
    {
        /// <summary>
        /// 自动补齐：UserView 方法说明。
        /// </summary>
        public UserView() { InitializeComponent(); }

        // PasswordBox鏃犳硶鐩存帴鍙屽悜缁戝畾锛岄€氳繃code-behind浼犻€掑瘑鐮?
        private void PwdBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.UserViewModel vm)
                vm.Password = ((PasswordBox)sender).Password;
        }
    }
}
