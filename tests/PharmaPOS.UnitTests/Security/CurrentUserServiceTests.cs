using PharmaPOS.Application.Common.Models;
using PharmaPOS.Infrastructure.Security;

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
            Permissions = new[] { "sales.manage", "dashboard.view" }
        });

        Assert.True(service.IsAuthenticated);
        Assert.True(service.HasPermission("sales.manage"));
        Assert.False(service.HasPermission("settings.manage"));
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
