using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Lembretes;

/// <summary>Aviso flutuante no canto inferior direito; vários avisos empilham.</summary>
public partial class AlertWindow : Window
{
    private static readonly List<AlertWindow> Open = new();

    public Reminder Reminder { get; }
    /// <summary>null = aviso na hora; número = aviso antecipado (minutos antes).</summary>
    public int? MinutesBefore { get; }
    public event Action<AlertWindow, string>? ActionChosen;

    public AlertWindow(Reminder r, int? minutesBefore = null)
    {
        InitializeComponent();
        Reminder = r;
        MinutesBefore = minutesBefore;
        Msg.Text = r.Text;
        TimeText.Text = (r.Due.HasValue ? Reminder.FormatWhen(r.Due.Value) : "")
                        + (r.IsRecurring ? "  ·  ↻ " + r.RepeatText : "");

        if (r.HasTag)
        {
            TagRow.Visibility = Visibility.Visible;
            TagDot.Fill = r.TagBrush;
            TagText.Text = r.TagName;
            TagText.Foreground = r.TagBrush;
        }

        if (minutesBefore.HasValue)
        {
            // Aviso antecipado: "Daqui a 1 dia" — só informa; o aviso principal continua agendado.
            var info = (System.Windows.Media.Brush)FindResource("InfoBrush");
            Frame.BorderBrush = info;
            HeaderIcon.Foreground = info;
            HeaderText.Foreground = info;
            HeaderIcon.Text = "";
            var left = r.Due.HasValue ? r.Due.Value - DateTime.Now : TimeSpan.Zero;
            HeaderText.Text = "Daqui a " + Reminder.DescribeSpan(Math.Max(1, (int)Math.Round(left.TotalMinutes / RoundTo(left)) * RoundTo(left)));
            SnoozePanel.Visibility = Visibility.Collapsed;
            DoneBtn.Content = "Ok";
            DoneBtn.Tag = "ok";
        }

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width;
        Top = wa.Bottom - 180;

        Loaded += (_, _) => { Open.Add(this); LayoutAll(); };
        SizeChanged += (_, _) => LayoutAll();
        Closed += (_, _) => { Open.Remove(this); LayoutAll(); };    }

    /// <summary>Arredonda o "daqui a" para algo legível: minutos, horas ou dias.</summary>
    private static int RoundTo(TimeSpan t) =>
        t.TotalHours >= 23.5 ? 1440 : t.TotalMinutes >= 59.5 ? 60 : 1;

    private static void LayoutAll()
    {
        var wa = SystemParameters.WorkArea;
        double y = wa.Bottom;
        foreach (var w in Open)
        {
            y -= w.ActualHeight;
            w.Left = wa.Right - w.ActualWidth;
            w.Top = Math.Max(wa.Top, y);
        }
    }

    private void OnAction(object sender, RoutedEventArgs e) =>
        ActionChosen?.Invoke(this, (string)((FrameworkElement)sender).Tag);

    private void OnMsgClick(object sender, MouseButtonEventArgs e) => ActionChosen?.Invoke(this, "open");
}
