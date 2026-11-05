using Papey.Services;

var checks = 0;
void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
var nowruz = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc);
Check(PersianHelpers.ToPersianDateTime(nowruz) == "۱۴۰۵/۰۱/۰۱ ۰۳:۳۰", "UTC timestamps display as Persian calendar dates with Tehran time");
Check(PersianHelpers.ToPersianDateTime(new(2026, 3, 20, 21, 0, 0, DateTimeKind.Utc)) == "۱۴۰۵/۰۱/۰۱ ۰۰:۳۰", "Iran midnight advances the Persian date before UTC midnight");
Check(PersianHelpers.ToPersianDate(new(2025, 3, 20, 0, 0, 0, DateTimeKind.Utc)) == "۱۴۰۳/۱۲/۳۰", "Persian leap-year Esfand 30 is retained");
Check(PersianHelpers.ToPersianMonthDay(nowruz) == "۰۱/۰۱", "Chart labels use Persian months and days");
Check(PersianHelpers.ToPersianDateTime(nowruz.ToLocalTime()) == PersianHelpers.ToPersianDateTime(nowruz), "Local-kind timestamps format as the same instant, regardless of the host zone");
Check(PersianHelpers.ToPersianDateTime(DateTime.SpecifyKind(nowruz, DateTimeKind.Unspecified)) == PersianHelpers.ToPersianDateTime(nowruz), "Legacy database timestamps without a Kind marker are treated as UTC");
Check(PersianHelpers.ToIranTime(new(2026, 3, 20, 21, 0, 0, DateTimeKind.Utc)).Date == new DateTime(2026, 3, 21), "Admin filters use the same civil date shown to the user");
Check(!PersianHelpers.ToPersianDateTime(nowruz).Any(char.IsAsciiDigit), "Displayed date and time contain Persian digits throughout");
Console.WriteLine($"{checks} Persian date checks passed.");
