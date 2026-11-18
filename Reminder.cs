using System;
using System.Collections;
using System.ComponentModel;
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

    [JsonIgnore] public bool IsRecurring => Repeat != RepeatKind.None;
    [JsonIgnore] public string RepeatText => IsRecurring ? Recurrence.Describe(Repeat, RepeatMinutes, Anchor) : "";

    /// <summary>Recorrente: pula para a próxima ocorrência (depois de agora e da ocorrência atual).</summary>
    public void AdvanceToNext(DateTime now)
    {
        if (!IsRecurring) return;
        var after = Due.HasValue && Due.Value > now ? Due.Value : now;
        Due = Recurrence.Next(Repeat, RepeatMinutes, Anchor ?? Due ?? now, after);
        Notified = false;
        Done = false;
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
            if (diff.TotalHours < 24) s += " · em " + Span(diff, true);
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
