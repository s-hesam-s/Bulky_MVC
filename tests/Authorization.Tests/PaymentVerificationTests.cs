using BulkyBook.Models;
using BulkyBookWeb.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Stripe;
using Stripe.Checkout;
using Xunit;

namespace Authorization.Tests;

public class PaymentVerificationTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("session", false)]
    [InlineData("reference", false)]
    [InlineData("currency", false)]
    [InlineData("amount", false)]
    [InlineData("unpaid", false)]
    [InlineData("intent", false)]
    [InlineData("intentAmount", false)]
    [InlineData("intentCurrency", false)]
    [InlineData("intentStatus", false)]
    public async Task RequiresMatchingSessionAndSucceededIntent(string variant, bool accepted)
    {
        var client = new Mock<IStripeClient>(MockBehavior.Strict);
        client.SetupGet(c => c.ApiBase).Returns("https://api.stripe.com");
        var order = new OrderHeader { Id = 7, OrderTotal = 20, SessionId = "cs_fixture" };
        var session = new Session { Id = "cs_fixture", Mode = "payment", ClientReferenceId = "7", Currency = "usd",
            AmountTotal = 2000, PaymentStatus = "paid", PaymentIntentId = "pi_fixture" };
        var intent = new PaymentIntent { Id = "pi_fixture", Status = "succeeded", Currency = "usd", Amount = 2000, AmountReceived = 2000 };
        switch (variant)
        {
            case "session": session.Id = "other"; break;
            case "reference": session.ClientReferenceId = "8"; break;
            case "currency": session.Currency = "eur"; break;
            case "amount": session.AmountTotal = 1999; break;
            case "unpaid": session.PaymentStatus = "unpaid"; break;
            case "intent": intent.Id = "other"; break;
            case "intentAmount": intent.AmountReceived = 1999; break;
            case "intentCurrency": intent.Currency = "eur"; break;
            case "intentStatus": intent.Status = "requires_payment_method"; break;
        }
        client.Setup(c => c.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_fixture", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        client.Setup(c => c.RequestAsync<PaymentIntent>(HttpMethod.Get, "/v1/payment_intents/pi_fixture", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(intent);
        var service = new OrderPaymentService(new ConfigurationBuilder().Build(), client.Object);
        Assert.Equal(accepted, await service.VerifyAsync(order) is not null);
    }

    [Fact]
    public async Task CheckoutUsesStoredMinorUnitsAndStableIdempotencyKey()
    {
        var client = new Mock<IStripeClient>(MockBehavior.Strict);
        client.SetupGet(c => c.ApiBase).Returns("https://api.stripe.com");
        client.Setup(c => c.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
            It.Is<BaseOptions>(o => ((SessionCreateOptions)o).LineItems.Single().PriceData.UnitAmount == 1099 &&
                ((SessionCreateOptions)o).LineItems.Single().Quantity == 2 && ((SessionCreateOptions)o).ClientReferenceId == "7"),
            It.Is<RequestOptions>(o => o.IdempotencyKey == "bulky-order-7-checkout"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = "cs_fixture" });
        var service = new OrderPaymentService(new ConfigurationBuilder().Build(), client.Object);
        var order = new OrderHeader { Id = 7, OrderTotal = 21.98 };
        await service.CreateAsync(order, new[] { new OrderDetail { Count = 2, Price = 10.99 } }, "https://example.test/return", "https://example.test/cancel");
        client.VerifyAll();
        order.OrderTotal = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(order,
            new[] { new OrderDetail { Count = 2, Price = 10.99 } }, "https://example.test/return", "https://example.test/cancel"));
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("unpaid")]
    public async Task OnlyUnpaidMatchingCheckoutCanExpire(string paymentStatus)
    {
        var client = new Mock<IStripeClient>(MockBehavior.Strict);
        client.SetupGet(c => c.ApiBase).Returns("https://api.stripe.com");
        client.Setup(c => c.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_fixture", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = "cs_fixture", Mode = "payment", ClientReferenceId = "7", Currency = "usd", AmountTotal = 2000, Status = "open", PaymentStatus = paymentStatus });
        client.Setup(c => c.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions/cs_fixture/expire", It.IsAny<BaseOptions>(),
            It.Is<RequestOptions>(o => o.IdempotencyKey == "bulky-order-7-expire"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Status = "expired" });
        var service = new OrderPaymentService(new ConfigurationBuilder().Build(), client.Object);
        var order = new OrderHeader { Id = 7, OrderTotal = 20, SessionId = "cs_fixture" };
        if (paymentStatus == "paid") await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelUnpaidAsync(order));
        else await service.CancelUnpaidAsync(order);
        client.Verify(c => c.RequestAsync<Session>(HttpMethod.Post, It.IsAny<string>(), It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(paymentStatus == "paid" ? 0 : 1));
    }
}
