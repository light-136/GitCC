using SmartHMI.Core.Interfaces;

namespace SmartHMI.UI.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly IUserService _userService;
    private string _username = "admin";
    private string _password = "";
    private string _errorMessage = "";
    private bool _isLoggingIn;

    public string Username { get => _username; set => SetField(ref _username, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }
    public string ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }
    public bool IsLoggingIn { get => _isLoggingIn; set => SetField(ref _isLoggingIn, value); }

    public RelayCommand LoginCommand { get; }
    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IUserService userService)
    {
        _userService = userService;
        LoginCommand = new RelayCommand(Login,
            () => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) && !IsLoggingIn);
    }

    private void Login()
    {
        IsLoggingIn = true;
        ErrorMessage = "";
        if (_userService.Login(Username, Password))
        {
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ErrorMessage = "用户名或密码错误";
        }
        IsLoggingIn = false;
    }
}
