using System.Globalization;
using BulkyBook.Models;
using Stripe;
using Stripe.Checkout;

namespace BulkyBookWeb.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IConfiguration configuration;
    private readonly IStripeClient? client;

    public OrderPaymentService(IConfiguration configuration, IStripeClient? client = null)
    {
        this.configuration = configuration;
        this.client = client;
    }

    public async Task<Session> CreateAsync(OrderHeader order, IReadOnlyList<OrderDetail> details, string returnUrl, string cancelUrl)
    {
        if (OrderRules.ToMinorUnits(OrderRules.Total(details)) != OrderRules.ToMinorUnits(order.OrderTotal))
            throw new InvalidOperationException("Stored order totals do not match.");
        var sessions = new SessionService(getClient());
        if (!string.IsNullOrEmpty(order.SessionId))
        {
            var existing = await sessions.GetAsync(order.SessionId);
            if (!matches(order, existing) || existing.Status != "open")
                throw new InvalidOperationException("The checkout requires verification or reconciliation.");
            return existing;
        }
        return await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment", SuccessUrl = returnUrl, CancelUrl = cancelUrl,
            ClientReferenceId = order.Id.ToString(CultureInfo.InvariantCulture),
            LineItems = details.Select(line => new SessionLineItemOptions
            {
                Quantity = line.Count,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "usd", UnitAmount = OrderRules.ToMinorUnits(line.Price),
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = line.Product?.Title ?? $"Product {line.ProductId}" }
                }
            }).ToList()
        }, new RequestOptions { IdempotencyKey = $"bulky-order-{order.Id}-checkout" });
    }

    public async Task<PaymentVerification?> VerifyAsync(OrderHeader order)
    {
        if (string.IsNullOrWhiteSpace(order.SessionId)) return null;
        var stripe = getClient();
        var session = await new SessionService(stripe).GetAsync(order.SessionId);
        if (!matches(order, session) || session.PaymentStatus != "paid" || string.IsNullOrWhiteSpace(session.PaymentIntentId)) return null;
        if (!string.IsNullOrEmpty(order.PaymentIntentId) && order.PaymentIntentId != session.PaymentIntentId) return null;
        var intent = await new PaymentIntentService(stripe).GetAsync(session.PaymentIntentId);
        return intent.Id == session.PaymentIntentId && intent.Status == "succeeded" && intent.Currency == "usd" &&
            intent.AmountReceived == OrderRules.ToMinorUnits(order.OrderTotal) && intent.Amount == OrderRules.ToMinorUnits(order.OrderTotal)
            ? new PaymentVerification(session.Id, intent.Id) : null;
    }

    public async Task CancelUnpaidAsync(OrderHeader order)
    {
        if (string.IsNullOrEmpty(order.SessionId)) return;
        var sessions = new SessionService(getClient());
        var session = await sessions.GetAsync(order.SessionId);
        if (!matches(order, session) || session.PaymentStatus == "paid")
            throw new InvalidOperationException("Payment verification or refund reconciliation is required.");
        if (session.Status == "expired") return;
        if (session.Status != "open") throw new InvalidOperationException("Checkout cannot be cancelled.");
        var expired = await sessions.ExpireAsync(session.Id, requestOptions: new RequestOptions { IdempotencyKey = $"bulky-order-{order.Id}-expire" });
        if (expired.Status != "expired") throw new InvalidOperationException("Checkout expiry was not confirmed.");
    }

    private static bool matches(OrderHeader order, Session session) =>
        session.Id == order.SessionId && session.Mode == "payment" && session.Currency == "usd" &&
        session.ClientReferenceId == order.Id.ToString(CultureInfo.InvariantCulture) &&
        session.AmountTotal == OrderRules.ToMinorUnits(order.OrderTotal);

    private IStripeClient getClient()
    {
        if (client is not null) return client;
        var key = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configure Stripe development settings before checkout.");
        return new StripeClient(key);
    }
}
