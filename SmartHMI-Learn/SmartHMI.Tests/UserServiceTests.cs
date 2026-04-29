using SmartHMI.Core.EventBus;
using SmartHMI.Services;

namespace SmartHMI.Tests;

/// <summary>
/// UserService 用户服务单元测试
/// </summary>
public class UserServiceTests
{
    private static UserService CreateService() => new(new EventAggregator());

    [Fact]
    public void Login_WithValidCredentials_ShouldReturnTrue()
    {
        var svc = CreateService();
        Assert.True(svc.Login("admin", "admin123"));
    }

    [Fact]
    public void Login_WithInvalidPassword_ShouldReturnFalse()
    {
        var svc = CreateService();
        Assert.False(svc.Login("admin", "wrongpassword"));
    }

    [Fact]
    public void Login_WithNonExistentUser_ShouldReturnFalse()
    {
        var svc = CreateService();
        Assert.False(svc.Login("nobody", "password"));
    }

    [Fact]
    public void Login_ShouldSetCurrentUser()
    {
        var svc = CreateService();
        svc.Login("engineer", "eng123");

        Assert.NotNull(svc.CurrentUser);
        Assert.Equal("engineer", svc.CurrentUser.Username);
    }

    [Fact]
    public void Logout_ShouldClearCurrentUser()
    {
        var svc = CreateService();
        svc.Login("admin", "admin123");
        svc.Logout();

        Assert.Null(svc.CurrentUser);
    }

    [Fact]
    public void Login_ShouldPublishUserLoginEvent()
    {
        var bus = new EventAggregator();
        var svc = new UserService(bus);
        Core.Events.UserLoginEvent? received = null;
        bus.Subscribe<Core.Events.UserLoginEvent>(e => received = e);

        svc.Login("operator", "op123");

        Assert.NotNull(received);
        Assert.True(received.IsLogin);
        Assert.Equal("operator", received.User?.Username);
    }

    [Fact]
    public void Logout_ShouldPublishUserLoginEventWithIsLoginFalse()
    {
        var bus = new EventAggregator();
        var svc = new UserService(bus);
        svc.Login("admin", "admin123");

        Core.Events.UserLoginEvent? received = null;
        bus.Subscribe<Core.Events.UserLoginEvent>(e => received = e);
        svc.Logout();

        Assert.NotNull(received);
        Assert.False(received.IsLogin);
        Assert.Null(received.User);
    }

    [Fact]
    public void GetAllUsers_ShouldReturnSeededUsers()
    {
        var svc = CreateService();
        var users = svc.GetAllUsers();

        Assert.Equal(4, users.Count);
        Assert.Contains(users, u => u.Username == "admin");
        Assert.Contains(users, u => u.Username == "engineer");
        Assert.Contains(users, u => u.Username == "operator");
        Assert.Contains(users, u => u.Username == "viewer");
    }
}
