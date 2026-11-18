using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lembretes;

public enum RepeatKind
{
    None,
    Daily,
    Weekdays,
    Weekly,
    Monthly,
    Interval
}

public static class Recurrence
{
    private static readonly string[] DayNames = { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" };

    /// <summary>Primeira ocorrência estritamente depois de <paramref name="after"/>, seguindo o padrão de <paramref name="anchor"/>.</summary>
    public static DateTime Next(RepeatKind kind, int minutes, DateTime anchor, DateTime after)
    {
        var tod = anchor.TimeOfDay;
        switch (kind)
        {
            case RepeatKind.Interval:
            {
                var step = TimeSpan.FromMinutes(Math.Max(1, minutes));
                if (anchor > after) return anchor;
                long n = (after - anchor).Ticks / step.Ticks + 1;
                return anchor + TimeSpan.FromTicks(step.Ticks * n);
            }
            case RepeatKind.Daily:
            {
                var d = after.Date + tod;
                if (d <= after) d = d.AddDays(1);
                return d;
            }
            case RepeatKind.Weekdays:
            {
                var d = after.Date + tod;
                if (d <= after) d = d.AddDays(1);
                while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
                return d;
            }
            case RepeatKind.Weekly:
            {
                var d = after.Date + tod;
                int diff = ((int)anchor.DayOfWeek - (int)d.DayOfWeek + 7) % 7;
                d = d.AddDays(diff);
                if (d <= after) d = d.AddDays(7);
                return d;
            }
            case RepeatKind.Monthly:
            {
                var month = new DateTime(after.Year, after.Month, 1);
                for (int i = 0; i < 24; i++, month = month.AddMonths(1))
                {
                    int day = Math.Min(anchor.Day, DateTime.DaysInMonth(month.Year, month.Month));
                    var d = new DateTime(month.Year, month.Month, day) + tod;
                    if (d > after) return d;
                }
                return after.AddMonths(1);
            }
            default:
                return anchor;
        }
    }

    public static string Describe(RepeatKind kind, int minutes, DateTime? anchor)
    {
        string at = anchor.HasValue ? $" às {anchor.Value:HH:mm}" : "";
        return kind switch
        {
            RepeatKind.Daily => "todo dia" + at,
            RepeatKind.Weekdays => "dias úteis" + at,
            RepeatKind.Weekly when anchor.HasValue =>
                (anchor.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "todo " : "toda ")
                + DayNames[(int)anchor.Value.DayOfWeek] + at,
            RepeatKind.Weekly => "toda semana",
            RepeatKind.Monthly when anchor.HasValue => $"todo dia {anchor.Value.Day}" + at,
            RepeatKind.Monthly => "todo mês",
            RepeatKind.Interval => "a cada " + DescribeMinutes(minutes),
            _ => ""
        };
    }

    public static string DescribeMinutes(int minutes)
    {
        if (minutes % 1440 == 0) return minutes == 1440 ? "1 dia" : $"{minutes / 1440} dias";
        if (minutes % 60 == 0) return $"{minutes / 60} h";
        if (minutes > 60) return $"{minutes / 60} h {minutes % 60} min";
        return $"{minutes} min";
    }

    // ------------------------------------------------------------------
    //  Montagem do agendamento a partir do campo "quando?" + menu de repetição
    // ------------------------------------------------------------------

    public record Plan(DateTime? Due, RepeatKind Kind, int Minutes, DateTime? Anchor);

    /// <summary>
    /// Interpreta o texto (que pode conter repetição, ex.: "todo dia 9h", "a cada 2h")
    /// e combina com a repetição escolhida no menu. Retorna false se não entender.
    /// </summary>
    public static bool TryBuild(string? when, RepeatKind menuKind, int menuMinutes, DateTime now, out Plan plan)
    {
        plan = new Plan(null, RepeatKind.None, 0, null);

        RepeatKind kind;
        int minutes;
        DateTime? anchor;

        if (TryParseRepeatText(when, now, out var tk, out var tm, out var ta, out bool valid))
        {
            if (!valid) return false;
            kind = tk; minutes = tm; anchor = ta;
        }
        else
        {
            if (!WhenParser.TryParse(when, now, out var due)) return false;
            kind = menuKind;
            minutes = menuMinutes;
            if (kind == RepeatKind.None)
            {
                plan = new Plan(due, RepeatKind.None, 0, null);
                return true;
            }
            anchor = due ?? (kind == RepeatKind.Interval ? now.AddMinutes(Math.Max(1, minutes)) : now.Date.AddHours(9));
        }

        var a = anchor!.Value;
        var first = Next(kind, minutes, a, Max(now, a.AddTicks(-1)));
        plan = new Plan(first, kind, minutes, a);
        return true;
    }

    private static DateTime Max(DateTime x, DateTime y) => x > y ? x : y;

    private const RegexOptions O = RegexOptions.CultureInvariant;
    private static readonly Regex Every = new(@"^a cada (\d+)\s*(m|min\w*|h|hr|hora\w*|d|dia\w*)$", O);
    private static readonly Regex MonthDayShort = new(@"^todo dia (\d{1,2})(?![\d:h])(?:\s+(.*))?$", O);
    private static readonly Regex DailyRx = new(@"^(?:todo dia|todos os dias|diariamente|diario)(?:\s+(.*))?$", O);
    private static readonly Regex WeekdaysRx = new(@"^(?:dias uteis|dia util|seg a sex|segunda a sexta)(?:\s+(.*))?$", O);
    private static readonly Regex WeeklyDayRx = new(@"^(?:toda|todo)s?\s+(dom|seg|ter|qua|qui|sex|sab)[a-z]*(?:-feira)?s?(?:\s+(.*))?$", O);
    private static readonly Regex WeeklyRx = new(@"^(?:toda semana|semanal(?:mente)?)(?:\s+(.*))?$", O);
    private static readonly Regex MonthlyRx = new(@"^(?:todo mes|mensal(?:mente)?)(?:\s+dia\s+(\d{1,2}))?(?:\s+(.*))?$", O);
    private static readonly string[] DayKeys = { "dom", "seg", "ter", "qua", "qui", "sex", "sab" };

    /// <returns>true se o texto tem uma expressão de repetição; <paramref name="valid"/> diz se ela é válida.</returns>
    private static bool TryParseRepeatText(string? input, DateTime now, out RepeatKind kind, out int minutes,
        out DateTime? anchor, out bool valid)
    {
        kind = RepeatKind.None; minutes = 0; anchor = null; valid = false;
        var s = Regex.Replace(RemoveAccents((input ?? "").Trim().ToLowerInvariant()), @"\s+", " ");
        if (s.Length == 0) return false;

        Match m;
        if ((m = Every.Match(s)).Success)
        {
            int n = int.Parse(m.Groups[1].Value);
            char unit = m.Groups[2].Value[0];
            minutes = unit switch { 'h' => n * 60, 'd' => n * 1440, _ => n };
            kind = RepeatKind.Interval;
            valid = minutes > 0;
            if (valid) anchor = now.AddMinutes(minutes);
            return true;
        }
        if ((m = MonthDayShort.Match(s)).Success)
        {
            kind = RepeatKind.Monthly;
            return BuildMonthly(int.Parse(m.Groups[1].Value), m.Groups[2].Value, now, out anchor, out valid);
        }
        if ((m = DailyRx.Match(s)).Success)
        {
            kind = RepeatKind.Daily;
            valid = TryTime(m.Groups[1].Value, now, out var tod);
            if (valid) anchor = now.Date + tod;
            return true;
        }
        if ((m = WeekdaysRx.Match(s)).Success)
        {
            kind = RepeatKind.Weekdays;
            valid = TryTime(m.Groups[1].Value, now, out var tod);
            if (valid) anchor = now.Date + tod;
            return true;
        }
        if ((m = WeeklyDayRx.Match(s)).Success)
        {
            kind = RepeatKind.Weekly;
            int dow = Array.IndexOf(DayKeys, m.Groups[1].Value);
            valid = dow >= 0 && TryTime(m.Groups[2].Value, now, out var tod);
            if (valid)
            {
                int diff = (dow - (int)now.DayOfWeek + 7) % 7;
                TryTime(m.Groups[2].Value, now, out tod);
                anchor = now.Date.AddDays(diff) + tod;
            }
            return true;
        }
        if ((m = WeeklyRx.Match(s)).Success)
        {
            kind = RepeatKind.Weekly;
            valid = TryTime(m.Groups[1].Value, now, out var tod);
            if (valid) anchor = now.Date + tod;
            return true;
        }
        if ((m = MonthlyRx.Match(s)).Success)
        {
            kind = RepeatKind.Monthly;
            int day = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : now.Day;
            return BuildMonthly(day, m.Groups[2].Value, now, out anchor, out valid);
        }
        return false;
    }

    private static bool BuildMonthly(int day, string rest, DateTime now, out DateTime? anchor, out bool valid)
    {
        anchor = null;
        valid = day is >= 1 and <= 31 && TryTime(rest, now, out var tod);
        if (valid)
        {
            TryTime(rest, now, out tod);
            // Âncora num mês de 31 dias para guardar o dia pedido (ex.: 31 → último dia nos meses curtos).
            var baseMonth = new DateTime(2026, 1, 1);
            anchor = new DateTime(now.Year, now.Month, Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month))) + tod;
            if (day > anchor.Value.Day) anchor = baseMonth.AddDays(day - 1) + tod; // preserva o dia 29–31
        }
        return true;
    }

    /// <summary>Horário do dia: vazio = 9h; aceita "9h", "às 14:30", "14:30".</summary>
    private static bool TryTime(string rest, DateTime now, out TimeSpan tod)
    {
        tod = TimeSpan.FromHours(9);
        rest = (rest ?? "").Trim();
        if (rest.Length == 0) return true;
        if (!WhenParser.TryParse(rest, now, out var d) || d == null) return false;
        if (rest.StartsWith("+") || rest.StartsWith("em ") || Regex.IsMatch(rest, @"^\d+\s*m")) return false;
        tod = d.Value.TimeOfDay;
        tod = new TimeSpan(tod.Hours, tod.Minutes, 0);
        return true;
    }

    private static string RemoveAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
