namespace PharmaPOS.Shared.Security;

/// <summary>Evaluates permission keys including module-level manage grants.</summary>
public static class PermissionResolver
{
    public static bool Has(IReadOnlyCollection<string> granted, string permissionKey)
    {
        if (granted.Contains(permissionKey))
            return true;

        var module = ModuleOf(permissionKey);
        return !string.IsNullOrEmpty(module) && granted.Contains($"{module}.manage");
    }

    public static bool HasAny(IReadOnlyCollection<string> granted, params string[] permissionKeys)
        => permissionKeys.Any(k => Has(granted, k));

    public static bool CanAccessModule(IReadOnlyCollection<string> granted, string module)
        => granted.Contains($"{module}.manage")
           || granted.Any(p => p.StartsWith($"{module}.", StringComparison.Ordinal));

    public static string ModuleOf(string permissionKey)
    {
        var dot = permissionKey.IndexOf('.');
        return dot > 0 ? permissionKey[..dot] : string.Empty;
    }
}
