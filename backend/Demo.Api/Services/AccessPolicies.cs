using Demo.Api.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Demo.Api.Services;

public static class AccessPolicies
{
    public const string BusinessRead = nameof(BusinessRead);
    public const string BusinessWrite = nameof(BusinessWrite);
    public const string BusinessDelete = nameof(BusinessDelete);
    public const string InventoryManage = nameof(InventoryManage);
    public const string OrderManage = nameof(OrderManage);
    public const string UserManage = nameof(UserManage);
    public const string AuditRead = nameof(AuditRead);

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(BusinessRead, p => p.RequireAuthenticatedUser().RequireRole(Enum.GetNames<UserRole>()));
        foreach (var policy in new[] { BusinessWrite, InventoryManage, OrderManage })
            options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin), nameof(UserRole.Manager)));
        foreach (var policy in new[] { BusinessDelete, UserManage, AuditRead })
            options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin)));
    }
}
