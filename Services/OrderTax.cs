using System.Globalization;
using Papey.Models;

namespace Papey.Services;

public static class OrderTax
{
    public const decimal RatePercent = 10m;
    public static decimal Calculate(decimal subtotal, bool enabled)
    {
        if (subtotal < 0) throw new ArgumentOutOfRangeException(nameof(subtotal));
        return enabled ? decimal.Round(subtotal * RatePercent / 100m, 0, MidpointRounding.AwayFromZero) : 0;
    }
    public static void Apply(Order order, bool enabled)
    {
        order.TaxEnabled = enabled;
        order.TaxRatePercent = enabled ? RatePercent : 0;
        order.TaxAmount = Calculate(order.TotalAmount, enabled);
        order.GrandTotal = order.TotalAmount + order.ShippingCost + order.TaxAmount;
    }
    public static string Label(bool enabled, decimal rate = RatePercent) => enabled
        ? $"مالیات ({PersianHelpers.ToPersianDigits(rate.ToString("0.##", CultureInfo.InvariantCulture))}٪)"
        : "مالیات (بدون مالیات)";
    public static string Label(Order order) => Label(order.TaxEnabled, order.TaxRatePercent);

    // Allocate the saved order tax for the official invoice, without rounding
    // every line independently or consulting the store's current tax setting.
    public static decimal[] Allocate(Order order)
    {
        var result = new decimal[order.Items.Count];
        var subtotal = order.Items.Sum(i => i.LineTotal);
        if (order.TaxAmount == 0 || subtotal <= 0) return result;
        var fractions = new decimal[result.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var share = order.TaxAmount * order.Items[i].LineTotal / subtotal;
            result[i] = decimal.Floor(share); fractions[i] = share - result[i];
        }
        var remaining = (int)(order.TaxAmount - result.Sum());
        foreach (var index in Enumerable.Range(0, result.Length).OrderByDescending(i => fractions[i]).ThenBy(i => i).Take(remaining)) result[index]++;
        return result;
    }
}
