using System;
using System.Threading;
using System.Windows;

namespace Lembretes;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instância única: se já estiver aberto, só traz a janela para frente.
        // Uma pasta de dados alternativa (LEMBRETES_DATA) roda como instância independente.
        var custom = Environment.GetEnvironmentVariable("LEMBRETES_DATA");
        var suffix = string.IsNullOrEmpty(custom) ? "" : "." + (uint)custom.ToLowerInvariant().GetHashCode();
        _mutex = new Mutex(true, "Lembretes.SingleInstance.b51e7c" + suffix, out bool created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Lembretes.Show.b51e7c" + suffix);
        if (!created)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Storage.Log(ex.Exception.ToString());
            ex.Handled = true;
        };

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        SessionEnding += (_, _) => main.PrepareExit();

        var waiter = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                Dispatcher.BeginInvoke(new Action(main.ShowMain));
            }
        }) { IsBackground = true };
        waiter.Start();
    }
}
