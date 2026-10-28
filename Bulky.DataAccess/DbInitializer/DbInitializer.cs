using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BulkyBook.DataAccess.DbInitializer;

public class DbInitializer : IDbInitializer
{
    private readonly RoleManager<IdentityRole> roles;
    private readonly ApplicationDbContext database;

    public DbInitializer(RoleManager<IdentityRole> roles, ApplicationDbContext database)
    {
        this.roles = roles;
        this.database = database;
    }

    public void Initialize()
    {
        if (database.Database.GetPendingMigrations().Any()) database.Database.Migrate();
        RoleBootstrap.EnsureAsync(roles).GetAwaiter().GetResult();
    }
}
