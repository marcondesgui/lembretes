using System;
using System.Text.RegularExpressions;

namespace Lembretes;

/// <summary>
/// Entende horários digitados em português:
///   (vazio)            → sem horário
///   14:30 · 14h · 9h30 → hoje nesse horário (ou amanhã, se já passou)
///   +15m · +2h · +1h30 · +1d · 15m · 15 min · em 2h → daqui a X
///   amanhã · amanhã 9h · amanhã às 14:30
///   25/12 · 25/12 10:00 · 25/12/2026 10h
/// </summary>
public static class WhenParser
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex DurationBody = new(
        @"^(?:(\d+)\s*d\p{L}*)?\s*(?:(\d+)\s*h\p{L}*)?\s*(?:(\d+)\s*(?:m\p{L}*)?)?$", O);
    private static readonly Regex MinutesOnly = new(@"^(\d+)\s*m(?:in\p{L}*)?$", O);
    private static readonly Regex TimeOfDay = new(
        @"^(amanhã|hoje)?\s*(?:(?:às|as|a)\s+)?(?:(\d{1,2})\s*(?::|h)\s*(\d{2})?)?$", O);
    private static readonly Regex DateTimeRx = new(
        @"^(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?(?:\s+(?:(?:às|as)\s+)?(\d{1,2})\s*(?::|h)\s*(\d{2})?)?$", O);

    public static bool TryParse(string? input, DateTime now, out DateTime? due)
    {
        due = null;
        var s = Regex.Replace((input ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        if (s.Length == 0) return true;
        s = s.Replace("amanha", "amanhã");

        try
        {
            // Duração com prefixo: "+15m", "em 2h", "+1h30"
            if (s.StartsWith("+") || s.StartsWith("em "))
            {
                var body = s.StartsWith("+") ? s[1..].Trim() : s[3..].Trim();
                var m = DurationBody.Match(body);
                if (m.Success && (m.Groups[1].Success || m.Groups[2].Success || m.Groups[3].Success))
                {
                    int d = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
                    int h = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                    int min = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                    // "+90" sem unidade = minutos
                    var span = new TimeSpan(d, h, min, 0);
                    if (span.TotalSeconds <= 0) return false;
                    due = now + span;
                    return true;
                }
                return false;
            }

            // "15m", "15 min" → daqui a 15 minutos
            var mo = MinutesOnly.Match(s);
            if (mo.Success)
            {
                due = now.AddMinutes(int.Parse(mo.Groups[1].Value));
                return true;
            }

            // "14:30", "9h", "amanhã", "amanhã 9h"
            var t = TimeOfDay.Match(s);
            if (t.Success && (t.Groups[1].Success || t.Groups[2].Success))
            {
                bool tomorrow = t.Groups[1].Value == "amanhã";
                int hour = t.Groups[2].Success ? int.Parse(t.Groups[2].Value) : 9;
                int minute = t.Groups[3].Success ? int.Parse(t.Groups[3].Value) : 0;
                if (hour > 23 || minute > 59) return false;
                var dt = now.Date.AddDays(tomorrow ? 1 : 0).AddHours(hour).AddMinutes(minute);
                if (!t.Groups[1].Success && dt <= now) dt = dt.AddDays(1);
                due = dt;
                return true;
            }

            // "25/12", "25/12 10:00", "25/12/2026 10h"
            var dm = DateTimeRx.Match(s);
            if (dm.Success)
            {
                int day = int.Parse(dm.Groups[1].Value);
                int month = int.Parse(dm.Groups[2].Value);
                bool hasYear = dm.Groups[3].Success;
                int year = hasYear ? int.Parse(dm.Groups[3].Value) : now.Year;
                if (year < 100) year += 2000;
                int hour = dm.Groups[4].Success ? int.Parse(dm.Groups[4].Value) : 9;
                int minute = dm.Groups[5].Success ? int.Parse(dm.Groups[5].Value) : 0;
                var dt = new DateTime(year, month, day, hour, minute, 0);
                if (!hasYear && dt < now.Date) dt = dt.AddYears(1);
                due = dt;
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false; // data inválida (ex.: 31/02)
        }

        return false;
    }
}
