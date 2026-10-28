using System.Security.Claims;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using BulkyBookWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace BulkyBookWeb.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize]
public class CartController : Controller
{
    private readonly IUnitOfWork unit;
    private readonly IOrderPaymentService payments;
    public ShoppingCartVM ShoppingCartVM { get; set; } = new();

    public CartController(IUnitOfWork unitOfWork, IOrderPaymentService payments)
    {
        unit = unitOfWork;
        this.payments = payments;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var userId = subject();
        if (userId is null) return Forbid();
        ShoppingCartVM = loadCart(userId);
        var images = unit.ProductImage.GetAll();
        foreach (var cart in ShoppingCartVM.ShoppingCartList)
            cart.Product.ProductImages = images.Where(image => image.ProductId == cart.ProductId).ToList();
        return View(ShoppingCartVM);
    }

    [HttpGet]
    public IActionResult Summary()
    {
        var userId = subject();
        if (userId is null) return Forbid();
        var user = unit.ApplicationUser.Get(u => u.Id == userId);
        if (user is null) return Forbid();
        ShoppingCartVM = loadCart(userId);
        ShoppingCartVM.OrderHeader.Name = user.Name;
        ShoppingCartVM.OrderHeader.PhoneNumber = user.PhoneNumber;
        ShoppingCartVM.OrderHeader.StreetAddress = user.StreetAddress;
        ShoppingCartVM.OrderHeader.City = user.City;
        ShoppingCartVM.OrderHeader.State = user.State;
        ShoppingCartVM.OrderHeader.PostalCode = user.PostalCode;
        return View(ShoppingCartVM);
    }

    [HttpPost, ActionName("Summary")]
    public async Task<IActionResult> SummaryPost([Bind(Prefix = "OrderHeader")] CheckoutInput input)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = subject();
        if (userId is null) return Forbid();
        var user = unit.ApplicationUser.Get(u => u.Id == userId);
        if (user is null) return Forbid();
        try
        {
            var created = unit.ExecuteInTransaction(() =>
            {
                var carts = unit.ShoppingCart.GetAll(c => c.ApplicationUserId == userId, includeProperties: "Product").ToList();
                var details = carts.Select(c => new OrderDetail { ProductId = c.ProductId, Count = c.Count, Price = OrderRules.UnitPrice(c) }).ToList();
                var order = new OrderHeader
                {
                    ApplicationUserId = userId, OrderDate = DateTime.UtcNow, OrderTotal = OrderRules.Total(details),
                    Name = input.Name, PhoneNumber = input.PhoneNumber, StreetAddress = input.StreetAddress,
                    City = input.City, State = input.State, PostalCode = input.PostalCode,
                    PaymentStatus = user.CompanyId.GetValueOrDefault() == 0 ? SD.PaymentStatusPending : SD.PaymentStatusDelayedPayment,
                    OrderStatus = user.CompanyId.GetValueOrDefault() == 0 ? SD.StatusPending : SD.StatusApproved
                };
                unit.OrderHeader.Add(order);
                unit.Save();
                foreach (var line in details) { line.OrderHeaderId = order.Id; unit.OrderDetail.Add(line); }
                if (order.PaymentStatus == SD.PaymentStatusDelayedPayment) unit.ShoppingCart.RemoveRange(carts);
                unit.Save();
                return order;
            });
            if (created.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                HttpContext.Session.Remove(SD.SessionCart);
                return RedirectToAction(nameof(OrderConfirmation), new { id = created.Id });
            }
            return await createCheckout(created);
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or OverflowException)
        {
            return StatusCode(502, "Checkout could not be completed. Review the saved order before retrying.");
        }
    }

    [HttpGet]
    public IActionResult OrderConfirmation(int id)
    {
        var order = unit.OrderHeader.Get(o => o.Id == id);
        if (order is null) return NotFound();
        if (!owns(order)) return Forbid();
        if (order.OrderStatus == SD.StatusCancelled) return Conflict("The order is cancelled.");
        ViewData["PaymentPending"] = order.PaymentStatus != SD.PaymentStatusApproved && order.PaymentStatus != SD.PaymentStatusDelayedPayment;
        return View(id);
    }

    [HttpPost]
    public async Task<IActionResult> VerifyPayment(int id)
    {
        var order = unit.OrderHeader.Get(o => o.Id == id);
        if (order is null) return NotFound();
        if (!owns(order)) return Forbid();
        if (order.PaymentStatus == SD.PaymentStatusApproved) return RedirectToAction(nameof(OrderConfirmation), new { id });
        if (!OrderRules.CanPay(order)) return Conflict("The order cannot be paid in its current state.");
        try
        {
            var expectedTotal = order.OrderTotal;
            var verified = await payments.VerifyAsync(order);
            if (verified is null) return BadRequest("Payment is not complete or does not match this order.");
            return unit.ExecuteWithOrderLock<IActionResult>(id, () =>
            {
                var stored = unit.OrderHeader.Get(o => o.Id == id, tracked: true);
                if (stored is null) return NotFound();
                if (!owns(stored)) return Forbid();
                if (stored.PaymentStatus == SD.PaymentStatusApproved) return RedirectToAction(nameof(OrderConfirmation), new { id });
                if (!OrderRules.CanPay(stored) || stored.SessionId != verified.SessionId || stored.OrderTotal != expectedTotal)
                    return Conflict("The order changed and requires reconciliation.");
                var wasPending = stored.PaymentStatus == SD.PaymentStatusPending;
                stored.PaymentIntentId = verified.PaymentIntentId;
                stored.PaymentStatus = SD.PaymentStatusApproved;
                stored.PaymentDate = DateTime.UtcNow;
                if (stored.OrderStatus == SD.StatusPending) stored.OrderStatus = SD.StatusApproved;
                unit.OrderHeader.Update(stored);
                if (wasPending) consumePurchasedCart(stored);
                unit.Save();
                HttpContext.Session.Remove(SD.SessionCart);
                return RedirectToAction(nameof(OrderConfirmation), new { id });
            });
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or OverflowException)
        {
            return StatusCode(502, "Payment verification is unavailable. The order was not marked paid.");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(int id)
    {
        var order = unit.OrderHeader.Get(o => o.Id == id);
        if (order is null) return NotFound();
        if (!owns(order)) return Forbid();
        if (!OrderRules.CanPay(order)) return Conflict("The order cannot be paid in its current state.");
        try { return await createCheckout(order); }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or OverflowException)
        { return StatusCode(502, "Checkout is unavailable. Review the existing order before retrying."); }
    }

    [HttpPost]
    public IActionResult Plus(int cartId) => changeQuantity(cartId, 1);

    [HttpPost]
    public IActionResult Minus(int cartId) => changeQuantity(cartId, -1);

    [HttpPost]
    public IActionResult Remove(int cartId) => changeQuantity(cartId, 0);

    private IActionResult changeQuantity(int cartId, int delta)
    {
        var cart = unit.ShoppingCart.Get(c => c.Id == cartId);
        if (cart is null) return NotFound();
        if (subject() is not string userId || cart.ApplicationUserId != userId) return Forbid();
        if (delta == 0 || (delta == -1 && cart.Count == 1)) unit.ShoppingCart.Remove(cart);
        else
        {
            var count = (long)cart.Count + delta;
            if (cart.Count < 1 || count < 1 || count > OrderRules.MaxQuantity) return BadRequest("Invalid quantity.");
            cart.Count = (int)count;
            unit.ShoppingCart.Update(cart);
        }
        unit.Save();
        HttpContext.Session.Remove(SD.SessionCart);
        return RedirectToAction(nameof(Index));
    }

    private ShoppingCartVM loadCart(string userId)
    {
        var carts = unit.ShoppingCart.GetAll(c => c.ApplicationUserId == userId, includeProperties: "Product").ToList();
        double total = 0;
        foreach (var cart in carts) { cart.Price = OrderRules.UnitPrice(cart); total += cart.Price * cart.Count; }
        return new ShoppingCartVM { ShoppingCartList = carts, OrderHeader = new OrderHeader { OrderTotal = total } };
    }

    private async Task<IActionResult> createCheckout(OrderHeader order)
    {
        if (!OrderRules.CanPay(order)) return Conflict("The order cannot be paid in its current state.");
        var expectedTotal = order.OrderTotal;
        var details = unit.OrderDetail.GetAll(d => d.OrderHeaderId == order.Id, includeProperties: "Product").ToList();
        var domain = $"{Request.Scheme}://{Request.Host}";
        var session = await payments.CreateAsync(order, details,
            domain + $"/Customer/Cart/OrderConfirmation?id={order.Id}", domain + $"/Admin/Order/Details?orderId={order.Id}");
        if (string.IsNullOrEmpty(session.Id) || !Uri.TryCreate(session.Url, UriKind.Absolute, out var location) || location.Scheme != "https")
            return StatusCode(502, "The provider did not return a valid checkout.");
        return unit.ExecuteWithOrderLock<IActionResult>(order.Id, () =>
        {
            var stored = unit.OrderHeader.Get(o => o.Id == order.Id, tracked: true);
            if (stored is null) return NotFound();
            if (!owns(stored)) return Forbid();
            if (!OrderRules.CanPay(stored) || stored.OrderTotal != expectedTotal ||
                (!string.IsNullOrEmpty(stored.SessionId) && stored.SessionId != session.Id))
                return Conflict("Checkout requires reconciliation because the order changed.");
            stored.SessionId = session.Id;
            unit.OrderHeader.Update(stored);
            unit.Save();
            Response.Headers.Location = session.Url;
            return StatusCode(303);
        });
    }

    private void consumePurchasedCart(OrderHeader order)
    {
        var purchased = unit.OrderDetail.GetAll(d => d.OrderHeaderId == order.Id).GroupBy(d => d.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Count));
        foreach (var cart in unit.ShoppingCart.GetAll(c => c.ApplicationUserId == order.ApplicationUserId))
        {
            if (!purchased.TryGetValue(cart.ProductId, out var count)) continue;
            if (cart.Count <= count) unit.ShoppingCart.Remove(cart);
            else { cart.Count -= count; unit.ShoppingCart.Update(cart); }
        }
    }

    private string? subject() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool owns(OrderHeader order) => subject() is string id && order.ApplicationUserId == id;
}
