using BulkyBook.Models;
using Stripe.Checkout;

namespace BulkyBookWeb.Services;

public interface IOrderPaymentService
{
    Task<Session> CreateAsync(OrderHeader order, IReadOnlyList<OrderDetail> details, string returnUrl, string cancelUrl);
    Task<PaymentVerification?> VerifyAsync(OrderHeader order);
    Task CancelUnpaidAsync(OrderHeader order);
}

public record PaymentVerification(string SessionId, string PaymentIntentId);
