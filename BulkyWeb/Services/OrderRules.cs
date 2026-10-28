using BulkyBook.Models;
using BulkyBook.Utility;

namespace BulkyBookWeb.Services;

public static class OrderRules
{
    public const int MaxQuantity = 1000;

    public static long ToMinorUnits(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) throw new InvalidOperationException("A positive order amount is required.");
        return checked((long)decimal.Round((decimal)amount * 100, 0, MidpointRounding.AwayFromZero));
    }

    public static double UnitPrice(ShoppingCart cart)
    {
        if (cart.Count < 1 || cart.Count > MaxQuantity || cart.Product is null)
            throw new InvalidOperationException("Invalid cart quantity or product.");
        var price = cart.Count <= 50 ? cart.Product.Price : cart.Count <= 100 ? cart.Product.Price50 : cart.Product.Price100;
        return ToMinorUnits(price) / 100d;
    }

    public static double Total(IEnumerable<OrderDetail> details)
    {
        long total = 0;
        var lines = details.ToList();
        if (lines.Count == 0) throw new InvalidOperationException("The order is empty.");
        foreach (var line in lines)
        {
            if (line.Count < 1 || line.Count > MaxQuantity) throw new InvalidOperationException("Invalid quantity.");
            total = checked(total + checked(ToMinorUnits(line.Price) * line.Count));
        }
        return total / 100d;
    }

    public static bool CanPay(OrderHeader order) =>
        (order.OrderStatus is SD.StatusPending or SD.StatusApproved or SD.StatusInProcess or SD.StatusShipped) &&
        (order.PaymentStatus is SD.PaymentStatusPending or SD.PaymentStatusDelayedPayment);

    public static bool CanProcess(OrderHeader order) => order.OrderStatus == SD.StatusApproved &&
        order.PaymentStatus is SD.PaymentStatusApproved or SD.PaymentStatusDelayedPayment;
}
