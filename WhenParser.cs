using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lembretes;

/// <summary>
/// Entende datas/horários digitados em português (acentos opcionais):
///   (vazio)                         → sem horário
///   14:30 · 14h · 9h30 · às 9h      → hoje nesse horário (ou amanhã, se já passou)
///   +15m · +2h · +1h30 · +3d · 15m · em 2h · daqui a 3 dias → daqui a X
///   hoje 18h · amanhã · amanhã 9h · depois de amanhã 14h
///   sexta · sexta 14h · próxima segunda 10h · terça que vem
///   dia 25 · dia 25 às 15h · dia 3 de janeiro
///   25 de dezembro · 25 de dezembro de 2027 às 10h
///   25/12 · 25/12 10:00 · 25/12/2027 às 10h
/// Sem horário explícito numa data, assume 9h.
/// </summary>
public static class WhenParser
{
    private const RegexOptions O = RegexOptions.CultureInvariant;
    private static readonly TimeSpan DefaultTime = TimeSpan.FromHours(9);

    // Horário opcional no fim: " 14h", " as 14:30", " a 9h", " 9" (só depois de um dia)
    private const string TimeOpt = @"(?:\s+(?:as\s+|a\s+)?(?<h>\d{1,2})(?:\s*(?::|h)\s*(?<m>\d{2})?)?)?";
    private const string Month = @"(?<mon>jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez)[a-z]*";

    private static readonly Regex DurationBody = new(
        @"^(?:(\d+)\s*d[a-z]*)?\s*(?:(\d+)\s*h[a-z]*)?\s*(?:(\d+)\s*(?:m[a-z]*)?)?$", O);
    private static readonly Regex MinutesOnly = new(@"^(\d+)\s*m(?:in[a-z]*)?$", O);
    private static readonly Regex TimeOnly = new(@"^(?:as\s+|a\s+)?(?<h>\d{1,2})\s*(?::|h)\s*(?<m>\d{2})?$", O);
    private static readonly Regex RelativeDay = new(@"^(?<d>hoje|amanha|depois de amanha)" + TimeOpt + "$", O);
    private static readonly Regex Weekday = new(
        @"^(?:(?<next>proxim[ao])\s+)?(?<dow>dom|seg|ter|qua|qui|sex|sab)[a-z]*(?:-feira)?(?<qv>\s+que vem)?" + TimeOpt + "$", O);
    private static readonly Regex DayOfMonth = new(
        @"^dia\s+(?<day>\d{1,2})(?:\s+(?:de\s+)?" + Month + @")?(?:\s+(?:de\s+)?(?<y>\d{4}))?" + TimeOpt + "$", O);
    private static readonly Regex DayMonthName = new(
        @"^(?<day>\d{1,2})\s+(?:de\s+)?" + Month + @"(?:\s+(?:de\s+)?(?<y>\d{4}))?" + TimeOpt + "$", O);
    private static readonly Regex DateNumeric = new(
        @"^(?<day>\d{1,2})/(?<mo>\d{1,2})(?:/(?<y>\d{2,4}))?" + TimeOpt + "$", O);

    private static readonly string[] DayKeys = { "dom", "seg", "ter", "qua", "qui", "sex", "sab" };
    private static readonly string[] MonthKeys = { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };

    public static bool TryParse(string? input, DateTime now, out DateTime? due)
    {
        due = null;
        var s = Normalize(input);
        if (s.Length == 0) return true;

        try
        {
            // Duração: "+15m", "em 2h", "daqui a 3 dias", "+1h30"
            string? body = s.StartsWith("+") ? s[1..] : s.StartsWith("em ") ? s[3..] : s.StartsWith("daqui a ") ? s[8..] : null;
            if (body != null)
            {
                var m = DurationBody.Match(body.Trim());
                if (!m.Success || !(m.Groups[1].Success || m.Groups[2].Success || m.Groups[3].Success)) return false;
                int d = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
                int h = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                int min = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                var span = new TimeSpan(d, h, min, 0);
                if (span.TotalSeconds <= 0) return false;
                due = now + span;
                return true;
            }

            Match x;

            // "15m", "15 min"
            if ((x = MinutesOnly.Match(s)).Success)
            {
                due = now.AddMinutes(int.Parse(x.Groups[1].Value));
                return true;
            }

            // "14:30", "9h", "às 9h" → hoje (ou amanhã se já passou)
            if ((x = TimeOnly.Match(s)).Success)
            {
                if (!TryTime(x, out var tod)) return false;
                var dt = now.Date + tod;
                if (dt <= now) dt = dt.AddDays(1);
                due = dt;
                return true;
            }

            // "hoje 18h", "amanhã", "depois de amanhã 14h"
            if ((x = RelativeDay.Match(s)).Success)
            {
                if (!TryTime(x, out var tod)) return false;
                int add = x.Groups["d"].Value switch { "amanha" => 1, "depois de amanha" => 2, _ => 0 };
                due = now.Date.AddDays(add) + tod;
                return true;
            }

            // "sexta 14h", "próxima segunda", "terça que vem 10h"
            if ((x = Weekday.Match(s)).Success)
            {
                if (!TryTime(x, out var tod)) return false;
                int dow = Array.IndexOf(DayKeys, x.Groups["dow"].Value);
                int diff = (dow - (int)now.DayOfWeek + 7) % 7;
                var dt = now.Date.AddDays(diff) + tod;
                bool forceNext = x.Groups["next"].Success || x.Groups["qv"].Success;
                if (diff == 0 && (forceNext || dt <= now)) dt = dt.AddDays(7);
                due = dt;
                return true;
            }

            // "dia 25", "dia 25 às 15h", "dia 3 de janeiro", "dia 3 de janeiro de 2027"
            if ((x = DayOfMonth.Match(s)).Success || (x = DayMonthName.Match(s)).Success)
            {
                if (!TryTime(x, out var tod)) return false;
                int day = int.Parse(x.Groups["day"].Value);
                bool hasMonth = x.Groups["mon"].Success;
                bool hasYear = x.Groups["y"].Success;
                int month = hasMonth ? Array.IndexOf(MonthKeys, x.Groups["mon"].Value) + 1 : now.Month;
                int year = hasYear ? int.Parse(x.Groups["y"].Value) : now.Year;
                if (day < 1 || day > 31) return false;
                // "dia 31" num mês de 30 dias → próximo mês que tenha dia 31
                if (!hasMonth && !hasYear && day > DateTime.DaysInMonth(year, month))
                {
                    due = NextMonthWithDay(now, day) + tod;
                    return true;
                }
                var dt = new DateTime(year, month, day) + tod;
                if (!hasYear && dt <= now)
                {
                    dt = hasMonth ? dt.AddYears(1) : NextMonthWithDay(now, day) + tod;
                }
                due = dt;
                return true;
            }

            // "25/12", "25/12 10:00", "25/12/2027 às 10h"
            if ((x = DateNumeric.Match(s)).Success)
            {
                if (!TryTime(x, out var tod)) return false;
                int day = int.Parse(x.Groups["day"].Value);
                int month = int.Parse(x.Groups["mo"].Value);
                bool hasYear = x.Groups["y"].Success;
                int year = hasYear ? int.Parse(x.Groups["y"].Value) : now.Year;
                if (year < 100) year += 2000;
                var dt = new DateTime(year, month, day) + tod;
                if (!hasYear && dt.Date < now.Date) dt = dt.AddYears(1);
                due = dt;
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false; // data inexistente (ex.: 31/02)
        }

        return false;
    }

    private static DateTime NextMonthWithDay(DateTime now, int day)
    {
        var m = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        for (int i = 0; i < 12; i++, m = m.AddMonths(1))
            if (day <= DateTime.DaysInMonth(m.Year, m.Month)) return new DateTime(m.Year, m.Month, day);
        throw new ArgumentException("dia inválido");
    }

    private static bool TryTime(Match x, out TimeSpan tod)
    {
        tod = DefaultTime;
        if (!x.Groups["h"].Success) return true;
        int h = int.Parse(x.Groups["h"].Value);
        int m = x.Groups["m"].Success ? int.Parse(x.Groups["m"].Value) : 0;
        if (h > 23 || m > 59) return false;
        tod = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>minúsculas, sem acentos, espaços simples.</summary>
    public static string Normalize(string? s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ");
    }
}
