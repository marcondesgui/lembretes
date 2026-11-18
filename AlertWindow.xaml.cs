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
    public event Action<AlertWindow, string>? ActionChosen;

    public AlertWindow(Reminder r)
    {
        InitializeComponent();
        Reminder = r;
        Msg.Text = r.Text;
        TimeText.Text = (r.Due.HasValue ? Reminder.FormatWhen(r.Due.Value) : "")
                        + (r.IsRecurring ? "  ·  ↻ " + r.RepeatText : "");

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width;
        Top = wa.Bottom - 180;

        Loaded += (_, _) => { Open.Add(this); LayoutAll(); };
        SizeChanged += (_, _) => LayoutAll();
        Closed += (_, _) => { Open.Remove(this); LayoutAll(); };
    }

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
