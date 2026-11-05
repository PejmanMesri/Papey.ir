using System.Globalization;
using System.Text;

namespace Papey.Services;

public static class PersianHelpers
{
    private static readonly CultureInfo Fa = new("fa-IR");
    private static readonly PersianCalendar Calendar = new();
    private static readonly TimeZoneInfo Iran = FindIranTimeZone();

    private static TimeZoneInfo FindIranTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
    }

    // Database timestamps are UTC, including legacy values with no Kind marker.
    public static DateTime ToIranTime(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime(), Iran);

    public static string ToPersianDate(DateTime value) => PersianDatePart(ToIranTime(value), true);
    public static string ToPersianMonthDay(DateTime value) => PersianDatePart(ToIranTime(value), false);
    public static string ToPersianDateTime(DateTime value)
    {
        var local = ToIranTime(value);
        return PersianDatePart(local, true) + " " + ToPersianDigits(local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
    private static string PersianDatePart(DateTime value, bool year) => ToPersianDigits(
        (year ? Calendar.GetYear(value).ToString("0000", CultureInfo.InvariantCulture) + "/" : "") +
        Calendar.GetMonth(value).ToString("00", CultureInfo.InvariantCulture) + "/" + Calendar.GetDayOfMonth(value).ToString("00", CultureInfo.InvariantCulture));

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

    /// <summary>Converts Persian (۰-۹) and Arabic (٠-٩) digits to Latin (0-9) — for search matching.</summary>
    public static string ToEnglishDigits(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c is >= '۰' and <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (c is >= '٠' and <= '٩') sb.Append((char)('0' + (c - '٠')));
            else sb.Append(c);
        }
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
