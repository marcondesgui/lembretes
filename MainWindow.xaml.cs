using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Lembretes;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Reminder> _items;
    private readonly ListCollectionView _view;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, AlertWindow> _alerts = new();
    private WinForms.NotifyIcon? _tray;
    private Reminder? _editing;
    private RepeatKind _repeatKind = RepeatKind.None;
    private int _repeatMinutes;
    private bool _exiting;
    private double _expandedHeight;
    private DateTime _lastRefresh = DateTime.MinValue;

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Lembretes";

    public MainWindow()
    {
        InitializeComponent();

        _settings = Storage.LoadSettings();
        _items = new ObservableCollection<Reminder>(Storage.LoadReminders());
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_items);
        _view.CustomSort = new ReminderComparer();
        List.ItemsSource = _view;

        ApplySettings();
        SetupTray();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // Abre já no modo salvo, sem reposicionar (a posição salva é a da janela compacta).
        if (_settings.Compact) SetCompact(true, keepEdge: false);

        SourceInitialized += (_, _) => EnableTaskbarMinimize();
        Loaded += (_, _) =>
        {
            EnsureOnScreen();
            Tick();
            if (!_settings.Compact) NewText.Focus();
        };
        Deactivated += (_, _) => SaveSettings();
        // Monitor desconectado / resolução alterada: traz a janela de volta.
        EventHandler onDisplay = (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(EnsureOnScreen));
        SystemEvents.DisplaySettingsChanged += onDisplay;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= onDisplay;

        UpdatePinButton();
        UpdateSummary();
    }

    // ============================================================
    //  Ciclo de alarmes
    // ============================================================

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var r in _items.ToList())
        {
            if (!r.Done && !r.Notified && r.Due.HasValue && r.Due.Value <= now)
                Fire(r);
        }

        if ((now - _lastRefresh).TotalSeconds >= 15)
        {
            _lastRefresh = now;
            foreach (var r in _items) r.Refresh();
            UpdateSummary();
        }
    }

    private void Fire(Reminder r)
    {
        r.Notified = true;
        r.Alerting = true;
        r.Refresh();
        Save();
        PlaySound();
        ShowAlert(r);
        FlashTaskbar();
        UpdateSummary();
    }

    private void ShowAlert(Reminder r)
    {
        if (_alerts.ContainsKey(r.Id)) return;
        var w = new AlertWindow(r);
        w.ActionChosen += OnAlertAction;
        w.Closed += (_, _) => _alerts.Remove(r.Id);
        _alerts[r.Id] = w;
        w.Show();
    }

    private void CloseAlert(Reminder r)
    {
        if (_alerts.TryGetValue(r.Id, out var w)) w.Close();
        r.Alerting = false;
    }

    private void OnAlertAction(AlertWindow w, string action)
    {
        var r = w.Reminder;
        switch (action)
        {
            case "open":
                ShowMain();
                return;
            case "done":
                if (r.IsRecurring) r.AdvanceToNext(DateTime.Now);
                else r.Done = true;
                break;
            case "close":
                // Recorrente: fechar o aviso conta como "visto" e já agenda a próxima.
                if (r.IsRecurring) r.AdvanceToNext(DateTime.Now);
                break;
            default:
                if (int.TryParse(action, out var minutes))
                {
                    r.Due = DateTime.Now.AddMinutes(minutes);
                    r.Notified = false;
                    r.Done = false;
                }
                break;
        }
        CloseAlert(r);
        Commit();
    }

    private static void PlaySound()
    {
        try
        {
            var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
            foreach (var name in new[] { "Windows Notify Calendar.wav", "Windows Notify System Generic.wav", "notify.wav" })
            {
                var path = Path.Combine(media, name);
                if (File.Exists(path))
                {
                    new System.Media.SoundPlayer(path).Play();
                    return;
                }
            }
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch { /* som é opcional */ }
    }

    // ============================================================
    //  Adicionar / editar
    // ============================================================

    private void OnAddClick(object sender, RoutedEventArgs e) => Submit();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_editing != null) EndEdit();
            else ClearInputs();
            e.Handled = true;
        }
    }

    private void ClearInputs()
    {
        NewText.Text = "";
        WhenText.Text = "";
        SetRepeat(RepeatKind.None, 0);
    }

    private void Submit()
    {
        var text = NewText.Text.Trim();
        if (text.Length == 0)
        {
            NewText.Focus();
            return;
        }
        if (!Recurrence.TryBuild(WhenText.Text, _repeatKind, _repeatMinutes, DateTime.Now, out var plan))
        {
            ShowHint();
            WhenText.Focus();
            WhenText.SelectAll();
            return;
        }

        if (_editing != null)
        {
            var r = _editing;
            r.Text = text;
            bool scheduleChanged = TruncMinute(r.Due) != TruncMinute(plan.Due)
                                   || r.Repeat != plan.Kind || r.RepeatMinutes != plan.Minutes;
            if (scheduleChanged)
            {
                r.Repeat = plan.Kind;
                r.RepeatMinutes = plan.Minutes;
                r.Anchor = plan.Anchor;
                r.Due = plan.Due;
                r.Notified = false;
                r.Done = false;
                CloseAlert(r);
            }
            EndEdit();
        }
        else
        {
            _items.Add(new Reminder
            {
                Text = text,
                Repeat = plan.Kind,
                RepeatMinutes = plan.Minutes,
                Anchor = plan.Anchor,
                Due = plan.Due
            });
            ClearInputs();
        }
        Commit();
        NewText.Focus();
    }

    // ---------- repetição (botão ↻) ----------

    private void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        var cm = new ContextMenu();
        void Add(string header, RepeatKind kind, int minutes)
        {
            var mi = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = _repeatKind == kind && (kind != RepeatKind.Interval || _repeatMinutes == minutes)
            };
            mi.Click += (_, _) => { SetRepeat(kind, minutes); WhenText.Focus(); };
            cm.Items.Add(mi);
        }
        Add("Não repetir", RepeatKind.None, 0);
        cm.Items.Add(new Separator());
        Add("Todo dia", RepeatKind.Daily, 0);
        Add("Dias úteis (seg a sex)", RepeatKind.Weekdays, 0);
        Add("Toda semana", RepeatKind.Weekly, 0);
        Add("Todo mês", RepeatKind.Monthly, 0);
        cm.Items.Add(new Separator());
        Add("A cada 15 min", RepeatKind.Interval, 15);
        Add("A cada 30 min", RepeatKind.Interval, 30);
        Add("A cada 1 hora", RepeatKind.Interval, 60);
        Add("A cada 2 horas", RepeatKind.Interval, 120);
        cm.Items.Add(new Separator());
        cm.Items.Add(new MenuItem
        {
            Header = "Dica: também dá pra digitar \"todo dia 9h\",\n\"toda segunda 10h\", \"a cada 45m\"…",
            IsEnabled = false
        });
        cm.PlacementTarget = (UIElement)sender;
        cm.Placement = PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void SetRepeat(RepeatKind kind, int minutes)
    {
        _repeatKind = kind;
        _repeatMinutes = minutes;
        bool on = kind != RepeatKind.None;
        RepeatBtn.Foreground = (Brush)FindResource(on ? "AccentBrush" : "MutedBrush");
        RepeatBtn.ToolTip = on ? "Repetir: " + Recurrence.Describe(kind, minutes, null) : "Repetir: não";
        ShowHint();
    }

    private static DateTime? TruncMinute(DateTime? d) =>
        d.HasValue ? new DateTime(d.Value.Year, d.Value.Month, d.Value.Day, d.Value.Hour, d.Value.Minute, 0) : null;

    private void BeginEdit(Reminder r)
    {
        if (_editing != null) _editing.IsEditing = false;
        if (_settings.Compact) SetCompact(false);
        _editing = r;
        r.IsEditing = true;
        NewText.Text = r.Text;
        WhenText.Text = r.Due.HasValue && !r.Done ? r.Due.Value.ToString("dd/MM/yyyy HH:mm") : "";
        SetRepeat(r.Repeat, r.RepeatMinutes);
        AddBtn.Content = "";
        AddBtn.ToolTip = "Salvar (Enter) · Esc cancela";
        NewText.Focus();
        NewText.SelectAll();
    }

    private void EndEdit()
    {
        if (_editing != null) _editing.IsEditing = false;
        _editing = null;
        AddBtn.Content = "";
        AddBtn.ToolTip = "Adicionar (Enter)";
        ClearInputs();
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        WhenText.Text = (string)((FrameworkElement)sender).Tag;
        NewText.Focus();
        NewText.CaretIndex = NewText.Text.Length;
    }

    private void OnWhenChanged(object sender, TextChangedEventArgs e) => ShowHint();

    private void ShowHint()
    {
        var when = WhenText.Text;
        if (string.IsNullOrWhiteSpace(when) && _repeatKind == RepeatKind.None)
        {
            Hint.Visibility = Visibility.Collapsed;
            return;
        }
        Hint.Visibility = Visibility.Visible;
        if (Recurrence.TryBuild(when, _repeatKind, _repeatMinutes, DateTime.Now, out var plan) && plan.Due.HasValue)
        {
            Hint.Text = "→ " + Reminder.Describe(plan.Due.Value)
                        + (plan.Kind != RepeatKind.None ? "  ·  ↻ " + Recurrence.Describe(plan.Kind, plan.Minutes, plan.Anchor) : "");
            Hint.Foreground = (Brush)FindResource("InfoBrush");
        }
        else
        {
            Hint.Text = "Não entendi o horário. Ex.: 14:30 · 9h · +15m · amanhã 9h · 25/12 10:00 · todo dia 9h · toda segunda 10h · a cada 2h";
            Hint.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    // ============================================================
    //  Ações nos itens
    // ============================================================

    private static Reminder? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as Reminder;

    private void OnToggleDone(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        if (r.Done)
        {
            CloseAlert(r);
            // Recorrente nunca fica "concluído": marca esta vez e agenda a próxima.
            if (r.IsRecurring) r.AdvanceToNext(DateTime.Now);
        }
        Commit();
    }

    private void OnSkipItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        CloseAlert(r);
        r.AdvanceToNext(DateTime.Now);
        Commit();
    }

    private void OnStopRepeatItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        r.StopRepeating();
        Commit();
    }

    private void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        if (_editing == r) EndEdit();
        CloseAlert(r);
        _items.Remove(r);
        Commit();
    }

    private void OnEditItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r != null) BeginEdit(r);
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var r = ItemOf(sender);
            if (r != null) BeginEdit(r);
            e.Handled = true;
        }
    }

    private void OnSnoozeItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        var tag = (string)((FrameworkElement)sender).Tag;
        r.Due = tag == "tomorrow" ? DateTime.Today.AddDays(1).AddHours(9) : DateTime.Now.AddMinutes(int.Parse(tag));
        r.Notified = false;
        r.Done = false;
        CloseAlert(r);
        Commit();
    }

    private void OnClearTimeItem(object sender, RoutedEventArgs e)
    {
        var r = ItemOf(sender);
        if (r == null) return;
        r.StopRepeating();
        r.Due = null;
        r.Notified = false;
        CloseAlert(r);
        Commit();
    }

    private void OnClearDone(object sender, RoutedEventArgs e)
    {
        foreach (var r in _items.Where(i => i.Done).ToList())
        {
            if (_editing == r) EndEdit();
            CloseAlert(r);
            _items.Remove(r);
        }
        Commit();
    }

    private void Commit()
    {
        Save();
        _view.Refresh();
        UpdateSummary();
    }

    private void Save() => Storage.SaveReminders(_items);

    private void UpdateSummary()
    {
        var pending = _items.Where(i => !i.Done).ToList();
        int done = _items.Count - pending.Count;
        int overdue = pending.Count(i => i.IsOverdue);

        CountText.Text = pending.Count.ToString();
        CountBadge.Background = (Brush)FindResource(overdue > 0 ? "DangerBrush" : "Surface2Brush");
        CountText.Foreground = overdue > 0 ? Brushes.White : (Brush)FindResource("MutedBrush");
        CountBadge.ToolTip = overdue > 0 ? $"{overdue} atrasado(s)" : null;

        string s = pending.Count == 1 ? "" : "s";
        FooterText.Text = pending.Count == 0
            ? (done > 0 ? "Tudo feito!" : "")
            : $"{pending.Count} pendente{s}" + (overdue > 0 ? $" · {overdue} atrasado{(overdue == 1 ? "" : "s")}" : "");
        ClearDoneBtn.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var next = pending.OrderBy(i => i.Due ?? DateTime.MaxValue).ThenBy(i => i.Created).FirstOrDefault();
        Subtitle.Text = next == null
            ? "Nada pendente"
            : next.Text + (next.Due.HasValue ? "  ·  " + Reminder.FormatWhen(next.Due.Value) : "");
        Subtitle.Foreground = (Brush)FindResource(next?.IsOverdue == true ? "DangerBrush" : "MutedBrush");

        Title = pending.Count > 0 ? $"Lembretes ({pending.Count})" : "Lembretes";
        if (_tray != null)
        {
            var t = pending.Count > 0 ? $"Lembretes — {pending.Count} pendente{s}" : "Lembretes";
            _tray.Text = t.Length > 63 ? t[..63] : t;
        }
    }

    // ============================================================
    //  Janela: cabeçalho, topo, compacto, menu
    // ============================================================

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SetCompact(!_settings.Compact);
            return;
        }
        try { DragMove(); } catch (InvalidOperationException) { }
        EnsureOnScreen();
        SaveSettings();
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => SetTopmost(!Topmost);

    private void SetTopmost(bool value)
    {
        Topmost = value;
        _settings.Topmost = value;
        UpdatePinButton();
        SaveSettings();
    }

    private void UpdatePinButton()
    {
        PinBtn.Content = Topmost ? "" : "";
        PinBtn.Foreground = (Brush)FindResource(Topmost ? "AccentBrush" : "MutedBrush");
        PinBtn.ToolTip = Topmost ? "Fixado no topo (clique para soltar)" : "Fixar sempre no topo";
    }

    private void OnCompactClick(object sender, RoutedEventArgs e) => SetCompact(!_settings.Compact);

    /// <param name="keepEdge">
    /// Se a janela está na metade de baixo da tela (ex.: encostada na barra de tarefas),
    /// mantém a borda de BAIXO fixa — ou seja, expande para cima e recolhe para baixo.
    /// </param>
    private void SetCompact(bool compact, bool keepEdge = true)
    {
        double? bottom = null;
        if (keepEdge && IsLoaded && ActualHeight > 0)
        {
            var wa = CurrentWorkArea();
            // Nunca ancora numa borda que já esteja fora da tela.
            if (Top + ActualHeight / 2 > wa.Top + wa.Height / 2) bottom = Math.Min(Top + ActualHeight, wa.Bottom);
        }

        if (compact)
        {
            if (Body.Visibility == Visibility.Visible && ActualHeight > 0) _expandedHeight = ActualHeight;
            Body.Visibility = Visibility.Collapsed;
            Subtitle.Visibility = Visibility.Visible;
            ResizeMode = ResizeMode.NoResize;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            CompactBtn.Content = "";
            CompactBtn.ToolTip = "Expandir";
        }
        else
        {
            double h = _expandedHeight > 0 ? _expandedHeight : _settings.Height;
            if (bottom.HasValue)
            {
                // Sobe primeiro para não "piscar" passando por cima da barra de tarefas.
                var wa = CurrentWorkArea();
                Top = Math.Max(wa.Top, bottom.Value - h);
                bottom = null;
            }
            SizeToContent = SizeToContent.Manual;
            Body.Visibility = Visibility.Visible;
            Subtitle.Visibility = Visibility.Collapsed;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinHeight = 180;
            Height = h;
            CompactBtn.Content = "";
            CompactBtn.ToolTip = "Recolher";
        }

        // A altura final só é conhecida após o layout: reancora a borda de baixo (ao recolher)
        // e garante que a janela continua inteira dentro da tela.
        var anchor = bottom;
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (anchor.HasValue) Top = anchor.Value - ActualHeight;
                EnsureOnScreen();
            }));
        }
        _settings.Compact = compact;
        if (IsLoaded) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(SaveSettings));
    }

    /// <summary>Puxa a janela de volta para dentro da área útil do monitor mais próximo.</summary>
    private void EnsureOnScreen()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        var wa = CurrentWorkArea();
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        double left = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
        double top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
        if (Math.Abs(left - Left) > 0.5) Left = left;
        if (Math.Abs(top - Top) > 0.5) Top = top;
    }

    /// <summary>Área útil (sem a barra de tarefas) do monitor onde a janela está, em unidades WPF.</summary>
    private Rect CurrentWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = PresentationSource.FromVisual(this);
        if (hwnd == IntPtr.Zero || src?.CompositionTarget == null) return SystemParameters.WorkArea;
        var wa = WinForms.Screen.FromHandle(hwnd).WorkingArea;
        var m = src.CompositionTarget.TransformFromDevice;
        return new Rect(m.Transform(new Point(wa.Left, wa.Top)), m.Transform(new Point(wa.Right, wa.Bottom)));
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        var cm = new ContextMenu();

        var top = new MenuItem { Header = "Sempre no topo", IsCheckable = true, IsChecked = Topmost };
        top.Click += (_, _) => SetTopmost(!Topmost);
        cm.Items.Add(top);

        var compact = new MenuItem { Header = "Modo compacto", IsCheckable = true, IsChecked = _settings.Compact };
        compact.Click += (_, _) => SetCompact(!_settings.Compact);
        cm.Items.Add(compact);

        var op = new MenuItem { Header = "Transparência" };
        foreach (var v in new[] { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5 })
        {
            var mi = new MenuItem
            {
                Header = v >= 1 ? "Opaco (100%)" : $"{v * 100:0}%",
                IsCheckable = true,
                IsChecked = Math.Abs(Opacity - v) < 0.01
            };
            mi.Click += (_, _) => { Opacity = v; _settings.Opacity = v; SaveSettings(); };
            op.Items.Add(mi);
        }
        cm.Items.Add(op);

        var startup = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true, IsChecked = IsStartupEnabled() };
        startup.Click += (_, _) => SetStartup(!IsStartupEnabled());
        cm.Items.Add(startup);

        cm.Items.Add(new Separator());

        var clear = new MenuItem { Header = "Limpar concluídos", IsEnabled = _items.Any(i => i.Done) };
        clear.Click += OnClearDone;
        cm.Items.Add(clear);

        var folder = new MenuItem { Header = "Abrir pasta de dados" };
        folder.Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(Storage.Dir);
            System.Diagnostics.Process.Start("explorer.exe", Storage.Dir);
        };
        cm.Items.Add(folder);

        cm.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Sair" };
        exit.Click += (_, _) => ExitApp();
        cm.Items.Add(exit);

        cm.PlacementTarget = (UIElement)sender;
        cm.Placement = PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => HideToTray();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    private void HideToTray()
    {
        SaveSettings();
        Hide();
        if (!_settings.HideHintShown && _tray != null)
        {
            _tray.ShowBalloonTip(4000, "Lembretes continua rodando",
                "Clique no ícone aqui na bandeja para abrir de novo. Para fechar de vez, use \"Sair\".",
                WinForms.ToolTipIcon.Info);
            _settings.HideHintShown = true;
            SaveSettings();
        }
    }

    public void ShowMain()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        EnsureOnScreen();
        Activate();
        if (!_settings.Compact) NewText.Focus();
    }

    public void PrepareExit()
    {
        _exiting = true;
        SaveSettings();
        Save();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    private void ExitApp()
    {
        PrepareExit();
        foreach (var a in _alerts.Values.ToList()) a.Close();
        Application.Current.Shutdown();
    }

    // ============================================================
    //  Configurações / posição
    // ============================================================

    private void ApplySettings()
    {
        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);
        _expandedHeight = Height;
        Topmost = _settings.Topmost;
        Opacity = Math.Clamp(_settings.Opacity, 0.3, 1.0);

        var wa = SystemParameters.WorkArea;
        double left = _settings.Left ?? wa.Right - Width - 24;
        double top = _settings.Top ?? wa.Top + 24;

        // Se a posição salva caiu longe de qualquer tela (ex.: monitor desconectado), volta pro canto.
        // Se só estiver um pouco fora, EnsureOnScreen (no Loaded) puxa para a borda mais próxima.
        double vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        if (left < vx - Width || left > vx + vw || top < vy - Height || top > vy + vh + 600)
        {
            left = wa.Right - Width - 24;
            top = wa.Top + 24;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SaveSettings()
    {
        if (!IsLoaded) return;
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = Width;
            _settings.Height = _settings.Compact ? _expandedHeight : ActualHeight;
        }
        _settings.Topmost = Topmost;
        _settings.Opacity = Opacity;
        Storage.SaveSettings(_settings);
    }

    private static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) is string;
    }

    private static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(RunValue, false);
    }

    // ============================================================
    //  Bandeja do sistema
    // ============================================================

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon { Text = "Lembretes", Visible = true };
        try
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            _tray.Icon = new System.Drawing.Icon(res.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Information;
        }

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowMain();
        };
        _tray.BalloonTipClicked += (_, _) => ShowMain();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => ShowMain());
        var trayTop = new WinForms.ToolStripMenuItem("Sempre no topo", null, (_, _) => SetTopmost(!Topmost));
        var trayStartup = new WinForms.ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => SetStartup(!IsStartupEnabled()));
        menu.Items.Add(trayTop);
        menu.Items.Add(trayStartup);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            trayTop.Checked = Topmost;
            trayStartup.Checked = IsStartupEnabled();
        };
        _tray.ContextMenuStrip = menu;
    }

    // ============================================================
    //  Win32: piscar na barra de tarefas e permitir minimizar pelo botão da taskbar
    // ============================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    private void FlashTaskbar()
    {
        if (!IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var fi = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = 3 | 12, // FLASHW_ALL | FLASHW_TIMERNOFG
            uCount = 5,
            dwTimeout = 0
        };
        FlashWindowEx(ref fi);
    }

    private void EnableTaskbarMinimize()
    {
        const int GWL_STYLE = -16;
        const long WS_MINIMIZEBOX = 0x00020000;
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style | WS_MINIMIZEBOX));
    }
}
