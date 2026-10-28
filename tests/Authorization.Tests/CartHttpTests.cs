using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBookWeb.Configuration;
using BulkyBookWeb.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class CartHttpTests
{
    [Theory]
    [InlineData("Plus")]
    [InlineData("Minus")]
    [InlineData("Remove")]
    [InlineData("Checkout")]
    [InlineData("VerifyPayment")]
    public async Task MutationGetIsRejectedAndPostRequiresAntiforgery(string action)
    {
        await using var fixture = await createApp();
        var client = fixture.App.GetTestClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync($"/Customer/Cart/{action}?cartId=11&id=7")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/Customer/Cart/{action}", new FormUrlEncodedContent(new Dictionary<string,string> { ["cartId"] = "11", ["id"] = "7" }))).StatusCode);
        fixture.Unit.Verify(u => u.Save(), Times.Never);
    }

    [Theory]
    [InlineData("owner", HttpStatusCode.Redirect)]
    [InlineData("other", HttpStatusCode.Forbidden)]
    public async Task ValidAntiforgeryStillRequiresCartOwner(string user, HttpStatusCode expected)
    {
        await using var fixture = await createApp();
        var client = fixture.App.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-User", user);
        var tokenResponse = await client.GetAsync("/fixture/token");
        var token = await tokenResponse.Content.ReadFromJsonAsync<Token>();
        client.DefaultRequestHeaders.Add("Cookie", tokenResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        var response = await client.PostAsync("/Customer/Cart/Plus", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["cartId"] = "11", ["__RequestVerificationToken"] = token.Value }));
        Assert.Equal(expected, response.StatusCode);
        fixture.Unit.Verify(u => u.Save(), Times.Exactly(user == "owner" ? 1 : 0));
    }

    [Fact]
    public async Task CheckoutBindingIgnoresForgedOwnerPriceStatusAndPaymentIdentifiers()
    {
        await using var fixture = await createApp();
        OrderHeader captured = null;
        Mock.Get(fixture.Unit.Object.OrderHeader).Setup(r => r.Add(It.IsAny<OrderHeader>())).Callback<OrderHeader>(order => { order.Id = 99; captured = order; });
        var client = fixture.App.GetTestClient();
        var tokenResponse = await client.GetAsync("/fixture/token");
        var token = await tokenResponse.Content.ReadFromJsonAsync<Token>();
        client.DefaultRequestHeaders.Add("Cookie", tokenResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        var response = await client.PostAsync("/Customer/Cart/Summary", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"] = token.Value,
            ["OrderHeader.Name"] = "Fixture", ["OrderHeader.PhoneNumber"] = "Fixture", ["OrderHeader.StreetAddress"] = "Fixture",
            ["OrderHeader.City"] = "Fixture", ["OrderHeader.State"] = "Fixture", ["OrderHeader.PostalCode"] = "Fixture",
            ["OrderHeader.ApplicationUserId"] = "attacker", ["OrderHeader.Id"] = "123", ["OrderHeader.OrderTotal"] = "0.01",
            ["OrderHeader.PaymentStatus"] = "Approved", ["OrderHeader.SessionId"] = "forged", ["OrderHeader.PaymentIntentId"] = "forged"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("owner", captured.ApplicationUserId);
        Assert.Equal(99, captured.Id);
        Assert.Equal(20, captured.OrderTotal);
        Assert.Equal(BulkyBook.Utility.SD.PaymentStatusDelayedPayment, captured.PaymentStatus);
        Assert.Null(captured.SessionId);
        Assert.Null(captured.PaymentIntentId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task AddCartRejectsOutOfRangeQuantityBeforePersistence(int quantity)
    {
        await using var fixture = await createApp();
        var client = fixture.App.GetTestClient();
        var response = await postWithToken(client, "/Customer/Home/Details", new() { ["ProductId"] = "3", ["Count"] = quantity.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        fixture.Unit.Verify(u => u.Save(), Times.Never);
    }

    [Fact]
    public async Task AddCartBindingCannotInjectEntityIdOwnerOrProductNavigation()
    {
        await using var fixture = await createApp();
        var carts = Mock.Get(fixture.Unit.Object.ShoppingCart);
        carts.Setup(c => c.Get(It.IsAny<Expression<Func<ShoppingCart,bool>>>(), It.IsAny<string>(), It.IsAny<bool>())).Returns((ShoppingCart)null);
        ShoppingCart captured = null;
        carts.Setup(c => c.Add(It.IsAny<ShoppingCart>())).Callback<ShoppingCart>(cart => captured = cart);
        var response = await postWithToken(fixture.App.GetTestClient(), "/Customer/Home/Details", new() {
            ["ProductId"] = "3", ["Count"] = "2", ["Id"] = "999", ["ApplicationUserId"] = "attacker",
            ["Product.Id"] = "999", ["Product.Price"] = "0.01", ["Product.Title"] = "Injected"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("owner", captured.ApplicationUserId);
        Assert.Equal(0, captured.Id);
        Assert.Equal(3, captured.ProductId);
        Assert.Equal(2, captured.Count);
        Assert.Null(captured.Product);
    }

    private static async Task<HttpResponseMessage> postWithToken(HttpClient client, string path, Dictionary<string,string> fields)
    {
        var response = await client.GetAsync("/fixture/token");
        var token = await response.Content.ReadFromJsonAsync<Token>();
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        fields["__RequestVerificationToken"] = token.Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<Fixture> createApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDistributedMemoryCache(); builder.Services.AddSession();
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSecureMvc().AddApplicationPart(typeof(BulkyBookWeb.Areas.Customer.Controllers.CartController).Assembly);
        var unit = new Mock<IUnitOfWork>();
        var carts = new Mock<IShoppingCartRepository>();
        carts.Setup(c => c.Get(It.IsAny<Expression<Func<ShoppingCart,bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new ShoppingCart { Id = 11, ApplicationUserId = "owner", Count = 1 });
        unit.SetupGet(u => u.ShoppingCart).Returns(carts.Object);
        var products = new Mock<IProductRepository>();
        products.Setup(p => p.Get(It.IsAny<Expression<Func<Product,bool>>>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(new Product { Id = 3, Price = 10 });
        unit.SetupGet(u => u.Product).Returns(products.Object);
        carts.Setup(c => c.GetAll(It.IsAny<Expression<Func<ShoppingCart,bool>>>(), It.IsAny<string>())).Returns(new[] {
            new ShoppingCart { Id = 11, ApplicationUserId = "owner", ProductId = 3, Count = 2,
                Product = new Product { Id = 3, Price = 10, Price50 = 8, Price100 = 7 } } });
        var users = new Mock<IApplicationUserRepository>();
        users.Setup(r => r.Get(It.IsAny<Expression<Func<ApplicationUser,bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new ApplicationUser { Id = "owner", CompanyId = 2 });
        unit.SetupGet(u => u.ApplicationUser).Returns(users.Object);
        unit.SetupGet(u => u.OrderHeader).Returns(new Mock<IOrderHeaderRepository>().Object);
        unit.SetupGet(u => u.OrderDetail).Returns(new Mock<IOrderDetailRepository>().Object);
        unit.Setup(u => u.ExecuteInTransaction(It.IsAny<Func<OrderHeader>>())).Returns((Func<OrderHeader> action) => action());
        builder.Services.AddSingleton(unit.Object);
        builder.Services.AddSingleton(new Mock<IOrderPaymentService>(MockBehavior.Strict).Object);
        var app = builder.Build();
        app.UseSession(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/fixture/token", (HttpContext context, IAntiforgery csrf) => new Token(csrf.GetAndStoreTokens(context).RequestToken));
        app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
        await app.StartAsync();
        return new Fixture(app, unit);
    }

    public record Token(string Value);
    private sealed record Fixture(WebApplication App, Mock<IUnitOfWork> Unit) : IAsyncDisposable
    { public ValueTask DisposeAsync() => App.DisposeAsync(); }

    public sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Fixture-User"].FirstOrDefault() ?? "owner") }, Scheme.Name)), Scheme.Name)));
    }
}
