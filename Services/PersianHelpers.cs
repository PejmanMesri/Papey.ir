using System.Globalization;
using System.Text;

namespace Papey.Services;

public static class PersianHelpers
{
    private static readonly CultureInfo Fa = new("fa-IR");

    /// <summary>Formats a Toman amount like ۱٬۶۵۰٬۰۰۰ تومان</summary>
    public static string ToToman(this decimal amount)
        => $"{ToPersianDigits(amount.ToString("N0", CultureInfo.InvariantCulture))} تومان";

    public static string ToPersianDigits(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
            sb.Append(c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c);
        return sb.ToString();
    }

    public static string StatusLabel(Models.OrderStatus status) => status switch
    {
        Models.OrderStatus.Pending => "در انتظار پرداخت",
        Models.OrderStatus.Paid => "پرداخت شده",
        Models.OrderStatus.Processing => "در حال پردازش",
        Models.OrderStatus.Shipped => "ارسال شده",
        Models.OrderStatus.Delivered => "تحویل داده شده",
        Models.OrderStatus.Cancelled => "لغو شده",
        Models.OrderStatus.PaymentFailed => "پرداخت ناموفق",
        _ => status.ToString()
    };
}
