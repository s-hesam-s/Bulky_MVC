using BulkyBook.Utility;
using Microsoft.AspNetCore.Identity;

namespace BulkyBook.DataAccess.DbInitializer;

public static class RoleBootstrap
{
    public static async Task EnsureAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var name in new[] { SD.Role_Customer, SD.Role_Admin, SD.Role_Employee, SD.Role_Company })
        {
            if (await roles.RoleExistsAsync(name)) continue;
            var result = await roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded) throw new InvalidOperationException("A required application role could not be initialized.");
        }
    }
}
