using PharmaPOS.Application.Common.Models;
using PharmaPOS.Infrastructure.Security;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.UnitTests.Security;

public class CurrentUserServiceTests
{
    [Fact]
    public void HasPermission_ReflectsSessionPermissions()
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(new UserSession
        {
            UserId = 1,
            Username = "admin",
            Permissions = new[] { AppConstants.Permissions.SalesManage, AppConstants.Permissions.DashboardView }
        });

        Assert.True(service.IsAuthenticated);
        Assert.True(service.HasPermission(AppConstants.Permissions.SalesManage));
        Assert.True(service.HasPermission(AppConstants.Permissions.SalesCreate));
        Assert.False(service.HasPermission(AppConstants.Permissions.SettingsManage));
        Assert.True(service.CanAccessModule("sales"));
    }

    [Fact]
    public void Clear_RemovesSession()
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(new UserSession { UserId = 1, Username = "x" });

        service.Clear();

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.False(service.HasPermission("anything"));
    }
}
