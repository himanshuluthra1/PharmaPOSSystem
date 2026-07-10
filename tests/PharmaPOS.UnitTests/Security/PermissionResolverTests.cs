using PharmaPOS.Shared.Constants;
using PharmaPOS.Shared.Security;

namespace PharmaPOS.UnitTests.Security;

public class PermissionResolverTests
{
    [Fact]
    public void Has_GrantsViaModuleManage()
    {
        var granted = new[] { AppConstants.Permissions.SalesManage };

        Assert.True(PermissionResolver.Has(granted, AppConstants.Permissions.SalesCreate));
        Assert.True(PermissionResolver.Has(granted, AppConstants.Permissions.SalesDiscount));
    }

    [Fact]
    public void CanAccessModule_RecognizesGranularKeys()
    {
        var granted = new[] { AppConstants.Permissions.SalesView, AppConstants.Permissions.SalesCreate };

        Assert.True(PermissionResolver.CanAccessModule(granted, "sales"));
        Assert.False(PermissionResolver.CanAccessModule(granted, "purchase"));
    }
}
