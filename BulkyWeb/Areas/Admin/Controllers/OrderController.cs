using System.Security.Claims;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using BulkyBookWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace BulkyBookWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class OrderController : Controller
{
    private readonly IUnitOfWork unit;
    private readonly IOrderPaymentService payments;
    public OrderVM OrderVM { get; set; } = new();
    public OrderController(IUnitOfWork unitOfWork, IOrderPaymentService payments)
    {
        unit = unitOfWork;
        this.payments = payments;
    }

    [HttpGet]
    public IActionResult Index() => subject() is null ? Forbid() : View();

    [HttpGet]
    public IActionResult Details(int orderId)
    {
        var order = unit.OrderHeader.Get(o => o.Id == orderId, includeProperties: "ApplicationUser");
        if (order is null) return NotFound();
        if (!canRead(order)) return Forbid();
        OrderVM = new OrderVM
        {
            OrderHeader = order,
            OrderDetails = unit.OrderDetail.GetAll(d => d.OrderHeaderId == orderId, includeProperties: "Product")
        };
        return View(OrderVM);
    }

    [HttpPost, Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
    public IActionResult UpdateOrderDetail([Bind(Prefix = "OrderHeader")] OrderContactInput input)
    {
        if (!isStaff()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return unit.ExecuteWithOrderLock<IActionResult>(input.Id, () =>
        {
            var order = unit.OrderHeader.Get(o => o.Id == input.Id, tracked: true);
            if (order is null) return NotFound();
            order.Name = input.Name; order.PhoneNumber = input.PhoneNumber;
            order.StreetAddress = input.StreetAddress; order.City = input.City;
            order.State = input.State; order.PostalCode = input.PostalCode;
            unit.OrderHeader.Update(order); unit.Save();
            return details(order.Id);
        });
    }

    [HttpPost, Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
    public IActionResult StartProcessing([FromForm(Name = "OrderHeader.Id")] int orderId)
    {
        if (!isStaff()) return Forbid();
        return unit.ExecuteWithOrderLock<IActionResult>(orderId, () =>
        {
            var order = unit.OrderHeader.Get(o => o.Id == orderId, tracked: true);
            if (order is null) return NotFound();
            if (order.OrderStatus == SD.StatusInProcess &&
                (order.PaymentStatus is SD.PaymentStatusApproved or SD.PaymentStatusDelayedPayment)) return details(orderId);
            if (!OrderRules.CanProcess(order)) return Conflict("The order cannot enter processing.");
            order.OrderStatus = SD.StatusInProcess;
            unit.OrderHeader.Update(order); unit.Save();
            return details(orderId);
        });
    }

    [HttpPost, Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
    public IActionResult ShipOrder([Bind(Prefix = "OrderHeader")] ShipmentInput input)
    {
        if (!isStaff()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return unit.ExecuteWithOrderLock<IActionResult>(input.Id, () =>
        {
            var order = unit.OrderHeader.Get(o => o.Id == input.Id, tracked: true);
            if (order is null) return NotFound();
            if (order.OrderStatus != SD.StatusInProcess ||
                (order.PaymentStatus != SD.PaymentStatusApproved && order.PaymentStatus != SD.PaymentStatusDelayedPayment))
                return Conflict("Only a processing order with approved payment terms can be shipped.");
            order.TrackingNumber = input.TrackingNumber; order.Carrier = input.Carrier;
            order.OrderStatus = SD.StatusShipped; order.ShippingDate = DateTime.UtcNow;
            if (order.PaymentStatus == SD.PaymentStatusDelayedPayment) order.PaymentDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
            unit.OrderHeader.Update(order); unit.Save();
            return details(order.Id);
        });
    }

    [HttpPost, Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
    public async Task<IActionResult> CancelOrder([FromForm(Name = "OrderHeader.Id")] int orderId)
    {
        if (!isStaff()) return Forbid();
        var order = unit.OrderHeader.Get(o => o.Id == orderId);
        if (order is null) return NotFound();
        if (order.OrderStatus == SD.StatusCancelled) return details(orderId);
        if (!canCancel(order)) return Conflict("Paid or shipped orders require a separate reconciliation process.");
        try
        {
            var expectedTotal = order.OrderTotal;
            var expectedSession = order.SessionId;
            await payments.CancelUnpaidAsync(order);
            return unit.ExecuteWithOrderLock<IActionResult>(orderId, () =>
            {
                var stored = unit.OrderHeader.Get(o => o.Id == orderId, tracked: true);
                if (stored is null) return NotFound();
                if (!canCancel(stored) || stored.SessionId != expectedSession || stored.OrderTotal != expectedTotal)
                    return Conflict("The order changed and requires reconciliation.");
                stored.OrderStatus = SD.StatusCancelled; stored.PaymentStatus = SD.PaymentStatusRejected;
                unit.OrderHeader.Update(stored); unit.Save();
                return details(orderId);
            });
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or OverflowException)
        { return StatusCode(502, "Cancellation could not be confirmed. Review provider state before retrying."); }
    }

    [HttpPost, ActionName("Details")]
    public IActionResult Details_PAY_NOW([FromForm(Name = "OrderHeader.Id")] int orderId)
    {
        var order = unit.OrderHeader.Get(o => o.Id == orderId);
        if (order is null) return NotFound();
        if (subject() is not string userId || order.ApplicationUserId != userId) return Forbid();
        if (!OrderRules.CanPay(order)) return Conflict("The order cannot be paid in its current state.");
        return RedirectToActionPreserveMethod("Checkout", "Cart", new { area = "Customer", id = orderId });
    }

    [HttpGet]
    public IActionResult PaymentConfirmation(int orderHeaderId)
    {
        var order = unit.OrderHeader.Get(o => o.Id == orderHeaderId);
        if (order is null) return NotFound();
        if (subject() is not string userId || order.ApplicationUserId != userId) return Forbid();
        if (order.OrderStatus == SD.StatusCancelled) return Conflict("The order is cancelled.");
        ViewData["PaymentPending"] = order.PaymentStatus != SD.PaymentStatusApproved;
        return View(orderHeaderId);
    }

    [HttpGet]
    public IActionResult GetAll(string status)
    {
        var userId = subject();
        if (userId is null) return Forbid();
        var orders = isStaff() ? unit.OrderHeader.GetAll(includeProperties: "ApplicationUser") :
            unit.OrderHeader.GetAll(o => o.ApplicationUserId == userId, includeProperties: "ApplicationUser");
        var selected = status switch
        {
            "pending" => SD.StatusPending, "inprocess" => SD.StatusInProcess,
            "completed" => SD.StatusShipped, "approved" => SD.StatusApproved, _ => null
        };
        if (selected is not null) orders = orders.Where(o => o.OrderStatus == selected);
        return Json(new { data = orders.Select(o => new {
            id = o.Id, name = o.Name, phoneNumber = o.PhoneNumber,
            applicationUser = new { email = o.ApplicationUser?.Email }, orderStatus = o.OrderStatus, orderTotal = o.OrderTotal
        }).ToList() });
    }

    private RedirectToActionResult details(int id) => RedirectToAction(nameof(Details), new { orderId = id });
    private string? subject() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool isStaff() => subject() is not null && (User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee));
    private bool canRead(OrderHeader order) => subject() is string userId && (order.ApplicationUserId == userId || isStaff());
    private static bool canCancel(OrderHeader order) =>
        (order.OrderStatus is SD.StatusPending or SD.StatusApproved or SD.StatusInProcess) &&
        (order.PaymentStatus is SD.PaymentStatusPending or SD.PaymentStatusDelayedPayment);
}
