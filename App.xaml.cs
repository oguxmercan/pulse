using System.Windows;
using Pulse.Core;

namespace Pulse;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--selftest"))
        {
            // Off the UI thread: the ETW continuations must not need the dispatcher we are blocking.
            var target = e.Args.SkipWhile(a => a != "--selftest").Skip(1).FirstOrDefault();
            Task.Run(() => SelfTest.RunAsync(target)).GetAwaiter().GetResult();
            return;
        }

        Store.SeedThemes();
        GpuTuner.Probe();
        GpuTuner.ResetStale(); // a previous run was killed before restoring clocks
        FpsTracker.CleanStaleSessions();

        // Any exit path puts fans and clocks back.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        DispatcherUnhandledException += (_, args) =>
        {
            Restore();
            MessageBox.Show(args.Exception.Message, "Pulse — beklenmeyen hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    static void Restore()
    {
        try
        {
            GpuTuner.ResetAll();
            GpuTuner.MarkActive(false);
        }
        catch { }
    }
}
