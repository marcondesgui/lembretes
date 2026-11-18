using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Lembretes;

public class Reminder : INotifyPropertyChanged
{
    private string _text = "";
    private DateTime? _due;
    private bool _done;
    private bool _alerting;
    private bool _editing;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Created { get; set; } = DateTime.Now;
    public bool Notified { get; set; }

    public string Text { get => _text; set { _text = value; OnChanged(); } }
    public DateTime? Due { get => _due; set { _due = value; OnChanged(); Refresh(); } }
    public bool Done { get => _done; set { _done = value; OnChanged(); Refresh(); } }

    private RepeatKind _repeat;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RepeatKind Repeat { get => _repeat; set { _repeat = value; OnChanged(); Refresh(); } }
    public int RepeatMinutes { get; set; }
    /// <summary>Referência do padrão de repetição (horário, dia da semana / do mês).</summary>
    public DateTime? Anchor { get; set; }

    private TagColor _tag;
    public TagColor Tag { get => _tag; set { _tag = value; OnChanged(); RefreshTag(); } }
    [JsonIgnore] public bool HasTag => Tag != TagColor.None;
    [JsonIgnore] public string TagName => Tags.NameOf(Tag);
    [JsonIgnore] public Brush TagBrush => Tags.BrushOf(Tag);

    public void RefreshTag()
    {
        OnChanged(nameof(HasTag));
        OnChanged(nameof(TagName));
        OnChanged(nameof(TagBrush));
    }

    [JsonIgnore] public bool IsRecurring => Repeat != RepeatKind.None;
    [JsonIgnore] public string RepeatText => IsRecurring ? Recurrence.Describe(Repeat, RepeatMinutes, Anchor) : "";

    /// <summary>Recorrente: pula para a próxima ocorrência (depois de agora e da ocorrência atual).</summary>
    public void AdvanceToNext(DateTime now)
    {
        if (!IsRecurring) return;
        var after = Due.HasValue && Due.Value > now ? Due.Value : now;
        Reschedule(Recurrence.Next(Repeat, RepeatMinutes, Anchor ?? Due ?? now, after), now);
        Done = false;
    }

    // ---------- avisos antecipados (eventos) ----------

    /// <summary>Minutos antes do horário em que também avisa (ex.: 1440 = 1 dia antes, 60 = 1 h antes).</summary>
    public List<int> RemindBefore { get; set; } = new();
    /// <summary>Quais avisos antecipados já foram mostrados para o horário atual.</summary>
    public List<int> FiredBefore { get; set; } = new();

    [JsonIgnore] public bool HasRemindBefore => RemindBefore.Count > 0 && Due.HasValue && !Done;
    [JsonIgnore] public string RemindBeforeText => DescribeBefore(RemindBefore);

    public static string DescribeBefore(IEnumerable<int> minutes)
    {
        var list = minutes.Distinct().OrderByDescending(m => m).Select(DescribeSpan).ToList();
        if (list.Count == 0) return "";
        var joined = list.Count == 1 ? list[0] : string.Join(", ", list.Take(list.Count - 1)) + " e " + list[^1];
        return "avisa " + joined + " antes";
    }

    public static string DescribeSpan(int minutes) =>
        minutes % 10080 == 0 ? (minutes == 10080 ? "1 semana" : $"{minutes / 10080} semanas")
        : Recurrence.DescribeMinutes(minutes);

    /// <summary>Muda o horário: zera o aviso principal e os antecipados (os que já passaram não disparam).</summary>
    public void Reschedule(DateTime? due, DateTime now)
    {
        Due = due;
        Notified = false;
        FiredBefore = new List<int>();
        SkipPassedPreAlerts(now);
        Refresh();
    }

    /// <summary>Marca como já mostrados os avisos antecipados cujo momento já passou.</summary>
    public void SkipPassedPreAlerts(DateTime now)
    {
        if (!Due.HasValue) return;
        foreach (var m in RemindBefore)
            if (!FiredBefore.Contains(m) && now >= Due.Value.AddMinutes(-m))
                FiredBefore.Add(m);
    }

    /// <summary>Aviso antecipado que deve disparar agora (o mais próximo do horário), ou null.</summary>
    public int? TakeDuePreAlert(DateTime now)
    {
        if (Done || !Due.HasValue || now >= Due.Value || RemindBefore.Count == 0) return null;
        int? fire = null;
        foreach (var m in RemindBefore.Distinct())
        {
            if (FiredBefore.Contains(m) || now < Due.Value.AddMinutes(-m)) continue;
            FiredBefore.Add(m);
            if (fire == null || m < fire) fire = m; // se vários passaram (app fechado), mostra só o mais próximo
        }
        return fire;
    }

    /// <summary>Seção da lista.</summary>
    [JsonIgnore]
    public string Group
    {
        get
        {
            if (Done) return "Concluídos";
            if (!Due.HasValue) return "Sem data";
            var d = Due.Value;
            var today = DateTime.Today;
            if (d <= DateTime.Now) return "Atrasados";
            if (d.Date == today) return "Hoje";
            if (d.Date == today.AddDays(1)) return "Amanhã";
            if (d.Date < today.AddDays(7)) return "Próximos 7 dias";
            return "Mais tarde";
        }
    }

    public void StopRepeating()
    {
        Repeat = RepeatKind.None;
        RepeatMinutes = 0;
        Anchor = null;
    }

    [JsonIgnore] public bool Alerting { get => _alerting; set { _alerting = value; OnChanged(); } }
    [JsonIgnore] public bool IsEditing { get => _editing; set { _editing = value; OnChanged(); } }
    [JsonIgnore] public bool HasDue => Due.HasValue;
    [JsonIgnore] public bool IsOverdue => Due.HasValue && !Done && Due.Value <= DateTime.Now;
    [JsonIgnore] public string DueText => Due.HasValue ? Describe(Due.Value, Done) : "";

    [JsonIgnore]
    public Brush DueBrush
    {
        get
        {
            if (!Due.HasValue || Done) return MutedBrush;
            var diff = Due.Value - DateTime.Now;
            if (diff.TotalSeconds <= 0) return DangerBrush;
            if (diff.TotalMinutes <= 15) return AccentBrush;
            return InfoBrush;
        }
    }

    public void Refresh()
    {
        OnChanged(nameof(HasDue));
        OnChanged(nameof(IsOverdue));
        OnChanged(nameof(DueText));
        OnChanged(nameof(DueBrush));
        OnChanged(nameof(IsRecurring));
        OnChanged(nameof(RepeatText));
        OnChanged(nameof(HasRemindBefore));
        OnChanged(nameof(RemindBeforeText));
        OnChanged(nameof(Group));
    }

    // ---------- formatação ----------

    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string FormatWhen(DateTime d)
    {
        var today = DateTime.Today;
        string day =
            d.Date == today ? "hoje" :
            d.Date == today.AddDays(1) ? "amanhã" :
            d.Date == today.AddDays(-1) ? "ontem" :
            d.ToString(d.Year == today.Year ? "ddd dd/MM" : "dd/MM/yyyy", PtBr);
        return $"{day} {d:HH:mm}";
    }

    public static string Describe(DateTime d, bool done = false)
    {
        var s = FormatWhen(d);
        if (done) return s;
        var diff = d - DateTime.Now;
        if (diff.TotalSeconds > 0)
        {
            int days = (d.Date - DateTime.Today).Days;
            if (diff.TotalHours < 24) s += " · em " + Span(diff, true);
            else if (days >= 2) s += days switch
            {
                < 14 => $" · em {days} dias",
                < 60 => $" · em {days / 7} semana{(days / 7 > 1 ? "s" : "")}" + (days % 7 > 0 ? $" e {days % 7} dia{(days % 7 > 1 ? "s" : "")}" : ""),
                _ => $" · em {days} dias"
            };
        }
        else
        {
            s += " · atrasado " + Span(-diff, false);
        }
        return s;
    }

    private static string Span(TimeSpan t, bool roundUp)
    {
        if (t.TotalMinutes < 1) return roundUp ? "1 min" : "agora";
        if (t.TotalHours < 1) return $"{(int)(roundUp ? Math.Ceiling(t.TotalMinutes) : Math.Floor(t.TotalMinutes))} min";
        if (t.TotalDays < 1) return t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min";
        int days = (int)t.TotalDays;
        return days == 1 ? "1 dia" : $"{days} dias";
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static readonly Brush MutedBrush = Frozen("#8C8F9A");
    private static readonly Brush DangerBrush = Frozen("#F06A63");
    private static readonly Brush AccentBrush = Frozen("#F5B83D");
    private static readonly Brush InfoBrush = Frozen("#7FA8F5");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Pendentes com horário (mais próximo primeiro), depois sem horário, depois concluídos.</summary>
public class ReminderComparer : IComparer
{
    public int Compare(object? x, object? y)
    {
        var a = (Reminder)x!;
        var b = (Reminder)y!;
        if (a.Done != b.Done) return a.Done ? 1 : -1;
        if (a.Done) return b.Created.CompareTo(a.Created);
        if (a.Due.HasValue != b.Due.HasValue) return a.Due.HasValue ? -1 : 1;
        if (a.Due.HasValue)
        {
            int c = a.Due!.Value.CompareTo(b.Due!.Value);
            if (c != 0) return c;
        }
        return a.Created.CompareTo(b.Created);
    }
}
