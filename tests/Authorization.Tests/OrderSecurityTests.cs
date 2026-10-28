using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using BulkyBookWeb.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using CartController = BulkyBookWeb.Areas.Customer.Controllers.CartController;
using OrderController = BulkyBookWeb.Areas.Admin.Controllers.OrderController;

namespace Authorization.Tests;

public class OrderSecurityTests
{
    private readonly Mock<IUnitOfWork> unit = new();
    private readonly Mock<IShoppingCartRepository> carts = new();
    private readonly Mock<IOrderHeaderRepository> orders = new();
    private readonly Mock<IOrderDetailRepository> details = new();
    private readonly Mock<IApplicationUserRepository> users = new();
    private readonly Mock<IOrderPaymentService> payments = new(MockBehavior.Strict);
    private readonly OrderHeader stored = new() { Id = 7, ApplicationUserId = "owner-a", OrderTotal = 20,
        OrderStatus = SD.StatusPending, PaymentStatus = SD.PaymentStatusPending, SessionId = "cs_fixture" };
    private readonly ShoppingCart cart = new() { Id = 11, ApplicationUserId = "owner-a", ProductId = 3, Count = 2,
        Product = new Product { Id = 3, Title = "Book", Price = 10, Price50 = 8, Price100 = 7 } };

    public OrderSecurityTests()
    {
        unit.SetupGet(u => u.ShoppingCart).Returns(carts.Object);
        unit.SetupGet(u => u.OrderHeader).Returns(orders.Object);
        unit.SetupGet(u => u.OrderDetail).Returns(details.Object);
        unit.SetupGet(u => u.ApplicationUser).Returns(users.Object);
        unit.Setup(u => u.ExecuteWithOrderLock(It.IsAny<int>(), It.IsAny<Func<IActionResult>>()))
            .Returns((int id, Func<IActionResult> action) => action());
        unit.Setup(u => u.ExecuteInTransaction(It.IsAny<Func<OrderHeader>>())).Returns((Func<OrderHeader> action) => action());
        orders.Setup(r => r.Get(It.IsAny<Expression<Func<OrderHeader, bool>>>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(stored);
        carts.Setup(r => r.Get(It.IsAny<Expression<Func<ShoppingCart, bool>>>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(cart);
        carts.Setup(r => r.GetAll(It.IsAny<Expression<Func<ShoppingCart, bool>>>(), It.IsAny<string>())).Returns(new[] { cart });
        details.Setup(r => r.GetAll(It.IsAny<Expression<Func<OrderDetail, bool>>>(), It.IsAny<string>()))
            .Returns(new[] { new OrderDetail { OrderHeaderId = 7, ProductId = 3, Count = 2, Price = 10, Product = cart.Product } });
    }

    [Theory]
    [InlineData("Plus")]
    [InlineData("Minus")]
    [InlineData("Remove")]
    public void AnotherUserCannotMutateCart(string action)
    {
        var controller = customer("owner-b");
        var result = action == "Plus" ? controller.Plus(11) : action == "Minus" ? controller.Minus(11) : controller.Remove(11);
        Assert.IsType<ForbidResult>(result);
        carts.Verify(r => r.Remove(It.IsAny<ShoppingCart>()), Times.Never);
        carts.Verify(r => r.Update(It.IsAny<ShoppingCart>()), Times.Never);
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Theory]
    [InlineData("owner-b", SD.Role_Customer)]
    [InlineData("staff", SD.Role_Admin)]
    public async Task OnlyOwnerCanInitiateOrVerifyPayment(string user, string role)
    {
        var controller = customer(user, role);
        Assert.IsType<ForbidResult>(await controller.Checkout(7));
        Assert.IsType<ForbidResult>(await controller.VerifyPayment(7));
        Assert.IsType<ForbidResult>(controller.OrderConfirmation(7));
        Assert.IsType<ForbidResult>(admin(user, role).PaymentConfirmation(7));
        Assert.IsType<ForbidResult>(admin(user, role).Details_PAY_NOW(7));
        payments.VerifyNoOtherCalls();
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Theory]
    [InlineData("owner-a", SD.Role_Customer, true)]
    [InlineData("owner-b", SD.Role_Customer, false)]
    [InlineData("staff", SD.Role_Admin, true)]
    [InlineData("staff", SD.Role_Employee, true)]
    public void OrderDetailsRequireOwnerOrStaff(string user, string role, bool allowed)
    {
        var result = admin(user, role).Details(7);
        if (allowed) Assert.IsType<ViewResult>(result); else Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void ConfirmationGetsNeverCallProviderOrPersistAnything()
    {
        Assert.IsType<ViewResult>(customer("owner-a").OrderConfirmation(7));
        Assert.IsType<ViewResult>(admin("owner-a").PaymentConfirmation(7));
        payments.VerifyNoOtherCalls();
        unit.Verify(u => u.Save(), Times.Never);
        carts.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<ShoppingCart>>()), Times.Never);
    }

    [Fact]
    public async Task MatchingPaidOrderIsAppliedOnceAndDoesNotConsumeCartOnRetry()
    {
        payments.Setup(p => p.VerifyAsync(stored)).ReturnsAsync(new PaymentVerification("cs_fixture", "pi_fixture"));
        var controller = customer("owner-a");
        Assert.IsType<RedirectToActionResult>(await controller.VerifyPayment(7));
        Assert.IsType<RedirectToActionResult>(await controller.VerifyPayment(7));
        Assert.Equal(SD.PaymentStatusApproved, stored.PaymentStatus);
        Assert.Equal(SD.StatusApproved, stored.OrderStatus);
        Assert.Equal("pi_fixture", stored.PaymentIntentId);
        payments.Verify(p => p.VerifyAsync(stored), Times.Once);
        carts.Verify(r => r.Remove(cart), Times.Once);
        unit.Verify(u => u.Save(), Times.Once);
    }

    [Fact]
    public async Task InvalidPaymentNeverApprovesOrClearsCart()
    {
        payments.Setup(p => p.VerifyAsync(stored)).ReturnsAsync((PaymentVerification)null);
        Assert.IsType<BadRequestObjectResult>(await customer("owner-a").VerifyPayment(7));
        Assert.Equal(SD.PaymentStatusPending, stored.PaymentStatus);
        unit.Verify(u => u.Save(), Times.Never);
        carts.Verify(r => r.Remove(It.IsAny<ShoppingCart>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentCancellationPreventsPaymentApproval()
    {
        payments.Setup(p => p.VerifyAsync(stored)).ReturnsAsync(new PaymentVerification("cs_fixture", "pi_fixture"));
        unit.Setup(u => u.ExecuteWithOrderLock(It.IsAny<int>(), It.IsAny<Func<IActionResult>>()))
            .Returns((int id, Func<IActionResult> action) => { stored.OrderStatus = SD.StatusCancelled; return action(); });
        Assert.IsType<ConflictObjectResult>(await customer("owner-a").VerifyPayment(7));
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Fact]
    public async Task ProviderFailureReturnsControlledErrorWithoutFalseApproval()
    {
        payments.Setup(p => p.VerifyAsync(stored)).ThrowsAsync(new InvalidOperationException("private-fixture-marker"));
        var result = Assert.IsType<ObjectResult>(await customer("owner-a").VerifyPayment(7));
        Assert.Equal(502, result.StatusCode);
        Assert.DoesNotContain("private-fixture-marker", JsonSerializer.Serialize(result.Value));
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Theory]
    [InlineData(SD.PaymentStatusApproved)]
    [InlineData(SD.PaymentStatusRejected)]
    public async Task PaidOrRejectedOrdersCannotBeCancelledAsUnpaid(string paymentStatus)
    {
        stored.PaymentStatus = paymentStatus;
        Assert.IsType<ConflictObjectResult>(await admin("staff", SD.Role_Admin).CancelOrder(7));
        payments.VerifyNoOtherCalls();
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Fact]
    public async Task CancellationExpiresProviderSessionBeforeChangingStoredState()
    {
        payments.Setup(p => p.CancelUnpaidAsync(stored)).Callback(() => Assert.Equal(SD.StatusPending, stored.OrderStatus)).Returns(Task.CompletedTask);
        Assert.IsType<RedirectToActionResult>(await admin("staff", SD.Role_Admin).CancelOrder(7));
        Assert.Equal(SD.StatusCancelled, stored.OrderStatus);
        Assert.Equal(SD.PaymentStatusRejected, stored.PaymentStatus);
        unit.Verify(u => u.Save(), Times.Once);
    }

    [Theory]
    [InlineData(SD.StatusPending, SD.PaymentStatusPending, false)]
    [InlineData(SD.StatusApproved, SD.PaymentStatusApproved, true)]
    [InlineData(SD.StatusApproved, SD.PaymentStatusDelayedPayment, true)]
    [InlineData(SD.StatusCancelled, SD.PaymentStatusApproved, false)]
    public void ProcessingRequiresApprovedOrderAndPaymentTerms(string state, string payment, bool allowed)
    {
        stored.OrderStatus = state; stored.PaymentStatus = payment;
        var result = admin("staff", SD.Role_Employee).StartProcessing(7);
        if (allowed) Assert.IsType<RedirectToActionResult>(result); else Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void CustomerCannotUseAdministrativeMutations()
    {
        Assert.IsType<ForbidResult>(admin("owner-a").StartProcessing(7));
        Assert.IsType<ForbidResult>(admin("owner-a").ShipOrder(new ShipmentInput { Id = 7 }));
        unit.Verify(u => u.Save(), Times.Never);
    }

    [Fact]
    public async Task CheckoutCreatesOrderFromStoredCartAndContactOnly()
    {
        users.Setup(r => r.Get(It.IsAny<Expression<Func<ApplicationUser, bool>>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new ApplicationUser { Id = "owner-a", CompanyId = 2 });
        OrderHeader created = null;
        orders.Setup(r => r.Add(It.IsAny<OrderHeader>())).Callback<OrderHeader>(o => { created = o; o.Id = 99; });
        Assert.IsType<RedirectToActionResult>(await customer("owner-a").SummaryPost(contact()));
        Assert.Equal(20, created.OrderTotal);
        Assert.Equal("owner-a", created.ApplicationUserId);
        Assert.Equal(SD.PaymentStatusDelayedPayment, created.PaymentStatus);
        Assert.Null(created.SessionId);
        Assert.Null(created.PaymentIntentId);
        Assert.Equal("Learner", created.Name);
        details.Verify(r => r.Add(It.Is<OrderDetail>(d => d.OrderHeaderId == 99 && d.ProductId == 3 && d.Count == 2 && d.Price == 10)), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidQuantitiesCannotBePriced(int count)
    {
        cart.Count = count;
        Assert.Throws<InvalidOperationException>(() => OrderRules.UnitPrice(cart));
    }

    [Theory]
    [InlineData(50, 10)]
    [InlineData(51, 8)]
    [InlineData(100, 8)]
    [InlineData(101, 7)]
    public void PricingUsesStoredQuantityTiers(int count, double expected)
    {
        cart.Count = count;
        Assert.Equal(expected, OrderRules.UnitPrice(cart));
    }

    private CartController customer(string user, string role = SD.Role_Customer) => new(unit.Object, payments.Object)
    { ControllerContext = new ControllerContext { HttpContext = context(user, role) } };
    private OrderController admin(string user, string role = SD.Role_Customer) => new(unit.Object, payments.Object)
    { ControllerContext = new ControllerContext { HttpContext = context(user, role) } };
    private static DefaultHttpContext context(string user, string role) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Role, role) }, "Fixture")),
        Session = new Mock<ISession>().Object
    };
    private static CheckoutInput contact() => new() { Name = "Learner", PhoneNumber = "Fixture", StreetAddress = "Fixture", City = "Fixture", State = "Fixture", PostalCode = "Fixture" };
}
