using System.Windows;
using System.Windows.Controls;

namespace SmartMES.UI.Views
{
    public partial class UserView : UserControl
    {
        public UserView() { InitializeComponent(); }

        // PasswordBox 无法直接双向绑定，通过 code-behind 传递密码
        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.UserViewModel vm)
                vm.Password = ((PasswordBox)sender).Password;
        }
    }
}
