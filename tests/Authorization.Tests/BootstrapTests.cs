using BulkyBook.DataAccess.DbInitializer;
using BulkyBook.Utility;
using BulkyBookWeb.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class BootstrapTests
{
    [Fact]
    public async Task MissingRolesAreCreatedIndependentlyOfExistingAdminRole()
    {
        var roles = createRoles();
        roles.Setup(r => r.RoleExistsAsync(SD.Role_Admin)).ReturnsAsync(true);
        await RoleBootstrap.EnsureAsync(roles.Object);
        roles.Verify(r => r.CreateAsync(It.Is<IdentityRole>(r => r.Name == SD.Role_Admin)), Times.Never);
        roles.Verify(r => r.CreateAsync(It.Is<IdentityRole>(r => r.Name == SD.Role_Customer)), Times.Once);
        roles.Verify(r => r.CreateAsync(It.Is<IdentityRole>(r => r.Name == SD.Role_Employee)), Times.Once);
        roles.Verify(r => r.CreateAsync(It.Is<IdentityRole>(r => r.Name == SD.Role_Company)), Times.Once);
    }

    [Fact]
    public async Task FailedRoleCreationStopsBootstrap()
    {
        var roles = createRoles();
        roles.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Failed());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RoleBootstrap.EnsureAsync(roles.Object));
    }

    [Fact]
    public void MissingFacebookSettingsDisableProviderAndIncompleteSettingsFailWithoutValues()
    {
        new ServiceCollection().AddOptionalFacebook(new ConfigurationBuilder().Build());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string> { ["Authentication:Facebook:AppId"] = "fixture-id" }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOptionalFacebook(config));
        Assert.DoesNotContain("fixture-id", error.Message);
    }

    private static Mock<RoleManager<IdentityRole>> createRoles()
    {
        var roles = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object, null, null, null, null);
        roles.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        roles.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);
        return roles;
    }
}
