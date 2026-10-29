using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace KalradyaCoop;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("tr-TR");
        try
        {
            if (args.Length == 2 && args[0] == "--worker") return HostWorker.RunAsync(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--install-mod") return MachineSetup.InstallModElevated(args[1]);
            if (args.Length == 2 && args[0] == "--verify") return Verification.RunAsync(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--verify-setup") return SetupVerification.RunAsync(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--verify-campaigns") return CampaignVerification.RunAsync(args[1]).GetAwaiter().GetResult();
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            using var mutex = new Mutex(true, "Local\\KalradyaCoop.Desktop.v1", out var first);
            using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\KalradyaCoop.Activate.v1");
            if (!first) { activation.Set(); return 0; }
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
            app.DispatcherUnhandledException += (_, e) => { Files.Log("UI error: " + e.Exception.Message); System.Windows.MessageBox.Show(e.Exception.Message, "Kalradya Co-op"); e.Handled = true; };
            var main = new MainWindow();
            var listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => app.Dispatcher.BeginInvoke(() => { main.Show(); main.WindowState = WindowState.Normal; main.Activate(); }), null, Timeout.Infinite, false);
            try { return app.Run(main); }
            finally { listener.Unregister(null); }
        }
        catch (Exception ex) { Files.Log("Fatal: " + ex); if (args.Length == 0) System.Windows.MessageBox.Show(ex.Message, "Kalradya Co-op"); return 1; }
    }
}
