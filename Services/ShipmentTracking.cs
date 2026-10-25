using System.Text.RegularExpressions;
using Papey.Models;

namespace Papey.Services;

public static class ShipmentTracking
{
    public static string NormalizeNumber(string? number) =>
        PersianHelpers.ToEnglishDigits(number ?? "").Trim().Replace(" ", "");

    public static bool IsValid(string? courier, string? number) =>
        !string.IsNullOrWhiteSpace(courier) && courier.Trim().Length <= 25
        && !courier.Any(char.IsControl)
        && Regex.IsMatch(NormalizeNumber(number), @"^[A-Za-z0-9-]{4,25}$");

    public static bool IsComplete(Order order) => IsValid(order.CourierName, order.TrackingNumber);
}
