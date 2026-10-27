using System.Security.Claims;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Utility;
using BulkyBookWeb.Areas.Identity.Pages.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class RegistrationSecurityTests
{
    [Theory]
    [InlineData(false, null, "Admin", "Customer")]
    [InlineData(true, "Customer", "Admin", "Customer")]
    [InlineData(false, "Admin", "Admin", "Customer")]
    [InlineData(true, "Admin", "Employee", "Employee")]
    [InlineData(true, "Admin", "Admin", "Admin")]
    [InlineData(true, "Admin", "Company", "Company")]
    public async Task RoleSelectionRequiresAnAuthenticatedAdministrator(bool authenticated, string callerRole,
        string requestedRole, string expectedRole)
    {
        var users = createUsers();
        var signIn = createSignIn(users);
        var page = createPage(users, signIn, authenticated, callerRole, requestedRole);
        var result = await page.OnPostAsync("/");
        Assert.IsType<PageResult>(result);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<IdentityUser>(), expectedRole), Times.Once);
        users.Verify(u => u.DeleteAsync(It.IsAny<IdentityUser>()), Times.Once);
        signIn.Verify(s => s.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        if (expectedRole != SD.Role_Company) Assert.Null(page.Input.CompanyId);
    }

    [Fact]
    public async Task AnAdministratorCannotCreateAnUnrecognizedRoleThroughRegistration()
    {
        var users = createUsers();
        var page = createPage(users, createSignIn(users), true, SD.Role_Admin, "unexpected-role");
        Assert.IsType<PageResult>(await page.OnPostAsync("/"));
        Assert.False(page.ModelState.IsValid);
        users.Verify(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never);
    }

    private static RegisterModel createPage(Mock<UserManager<IdentityUser>> users, Mock<SignInManager<IdentityUser>> signIn,
        bool authenticated, string callerRole, string requestedRole)
    {
        var store = new Mock<IUserEmailStore<IdentityUser>>();
        store.Setup(s => s.SetUserNameAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        store.Setup(s => s.SetEmailAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var roles = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object, null, null, null, null);
        var claims = callerRole == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.Role, callerRole) };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null)) };
        return new RegisterModel(users.Object, roles.Object, store.Object, signIn.Object,
            new Mock<ILogger<RegisterModel>>().Object, new Mock<IEmailSender>().Object, new Mock<IUnitOfWork>().Object)
        {
            PageContext = new PageContext { HttpContext = context },
            Input = new RegisterModel.InputModel
            {
                Email = "learner@example.invalid", Name = "Learner", Password = "NotARealCredential!123",
                ConfirmPassword = "NotARealCredential!123", Role = requestedRole, CompanyId = 123
            }
        };
    }

    private static Mock<UserManager<IdentityUser>> createUsers()
    {
        var users = new Mock<UserManager<IdentityUser>>(new Mock<IUserEmailStore<IdentityUser>>().Object,
            Options.Create(new IdentityOptions()), null, null, null, null, null, null, null);
        users.SetupGet(u => u.SupportsUserEmail).Returns(true);
        users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(u => u.AddToRoleAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Assignment failed" }));
        users.Setup(u => u.DeleteAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
        return users;
    }

    private static Mock<SignInManager<IdentityUser>> createSignIn(Mock<UserManager<IdentityUser>> users)
    {
        var manager = new Mock<SignInManager<IdentityUser>>(users.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<IdentityUser>>().Object, Options.Create(new IdentityOptions()),
            new Mock<ILogger<SignInManager<IdentityUser>>>().Object, new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<IdentityUser>>().Object);
        manager.Setup(s => s.GetExternalAuthenticationSchemesAsync()).ReturnsAsync(Array.Empty<AuthenticationScheme>());
        return manager;
    }
}
