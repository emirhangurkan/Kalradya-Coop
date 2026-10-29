using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;
namespace KalradyaCoop;

public sealed class SetupWindow : Window
{
    private readonly AppSettings settings;
    private readonly bool host;
    private readonly TextBlock summary = new() { FontSize = 15, LineHeight = 28, Margin = new Thickness(0, 15, 0, 18) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 48, Margin = new Thickness(0, 14, 0, 12) };
    private readonly TextBox gamePath = new() { IsReadOnly = true, Height = 42 };
    private readonly CheckBox eula = new() { Content = "Sunucu kullanım koşullarını okudum ve kabul ediyorum" };
    private readonly Button install = new() { Content = "Eksikleri hazırla", Margin = new Thickness(0, 14, 0, 8) };
    private readonly Button browse = new() { Content = "Oyun klasörünü seç", Margin = new Thickness(0, 8, 0, 0) };
    private readonly ProgressBar progress = new() { Height = 4, IsIndeterminate = true, Visibility = Visibility.Hidden };
    private readonly CancellationTokenSource cancellation = new();
    private bool busy;
    public SetupWindow(AppSettings settings, bool host)
    {
        this.settings = settings; this.host = host;
        Title = "Kalradya • Bu bilgisayarı hazırla"; Width = 680; Height = 690;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        Style = (Style)FindResource(typeof(Window)); WindowChrome.Install(this);
        var panel = new StackPanel { Margin = new Thickness(28) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Bannerlord'u hazırla", Style = (Style)FindResource("Heading"), FontSize = 26 });
        panel.Children.Add(new TextBlock { Text = host ? "Sunucu bu bilgisayarda açılacak." : "Yalnızca arkadaşının oyununa katılacaksın.", Margin = new Thickness(0, 7, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        panel.Children.Add(summary); panel.Children.Add(gamePath); panel.Children.Add(browse);
        panel.Children.Add(new TextBlock { Text = "Coop 0.1.6 · Bannerlord 1.4.8\nKurulum, Coop'u açar; War Sails, Birth and Death ve FastMode'u kapatır.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 15, 0, 12), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        if (host)
        {
            var license = new Button { Content = "Sunucu kullanım koşullarını oku", Style = (Style)FindResource("Quiet") };
            license.Click += (_, _) => ShowLicense();
            panel.Children.Add(license); panel.Children.Add(eula);
        }
        panel.Children.Add(status); panel.Children.Add(progress);
        install.Style = (Style)FindResource("Primary"); panel.Children.Add(install);
        var close = new Button { Content = "Kapat / indirmeyi iptal et", Style = (Style)FindResource("Quiet") };
        panel.Children.Add(close);
        browse.Click += (_, _) => { var dialog = new OpenFolderDialog { Title = "Modules klasörünü içeren Bannerlord klasörünü seç" }; if (dialog.ShowDialog(this) == true) { if (!MachineSetup.IsGame(dialog.FolderName)) { status.Text = "Bu klasörde Bannerlord bulunamadı."; return; } settings.GameDir = dialog.FolderName; settings.Store = ""; MachineSetup.Discover(settings); Refresh(); } };
        close.Click += (_, _) => { if (busy) { cancellation.Cancel(); status.Text = "İndirme iptal ediliyor; çalışan kurulumun tamamlanması bekleniyor…"; } else Close(); };
        install.Click += async (_, _) => await InstallAsync();
        Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Kurulum sürüyor. İptal düğmesini kullanabilirsin."; } };
        Closed += (_, _) => cancellation.Dispose();
        Refresh();
    }
    private void Refresh()
    {
        MachineSetup.Discover(settings);
        summary.Text = (MachineSetup.IsGame(settings.GameDir) ? "✓ Bannerlord · " + (settings.Store.Length > 0 ? settings.Store : "Klasör") + " · " + MachineSetup.GameVersion(settings.GameDir) : "○ Bannerlord · oyun klasörünü seç") + "\n"
            + (File.Exists(NetworkService.TailscaleExe) ? "✓ Tailscale hazır" : "○ Tailscale kurulacak") + "\n"
            + (MachineSetup.ModInstalled(settings) ? "✓ Coop hazır" : "○ Coop kurulacak · 13 MB")
            + (host ? "\n" + (File.Exists(settings.ServerExe) ? "✓ Sunucu hazır" : "○ Sunucu indirilecek · 4,4 GB") : "");
        gamePath.Text = settings.GameDir;
        eula.IsEnabled = File.Exists(Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "EULA.md"));
        if (MachineSetup.EulaAccepted(settings)) eula.IsChecked = true;
    }
    private void ShowLicense()
    {
        var root = Path.GetDirectoryName(settings.ServerExe)!;
        if (!File.Exists(Path.Combine(root, "EULA.md"))) { status.Text = "Koşullar sunucu paketi indirildiğinde burada açılacak."; return; }
        var text = File.ReadAllText(Path.Combine(root, "EULA.md")) + "\n\n" + File.ReadAllText(Path.Combine(root, "LICENSE.txt"));
        var dialog = new Window { Owner = this, Title = "Sunucu kullanım koşulları", Width = 800, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner, Style = (Style)FindResource(typeof(Window)) };
        dialog.Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
        WindowChrome.Install(dialog); dialog.ShowDialog();
    }
    private async Task InstallAsync()
    {
        if (busy) return;
        busy = true; install.IsEnabled = browse.IsEnabled = false; progress.Visibility = Visibility.Visible;
        try
        {
            if (!MachineSetup.IsGame(settings.GameDir)) throw new IOException("Önce satın alıp yüklediğin Bannerlord'un klasörünü seç.");
            Action<string> show = text => Dispatcher.Invoke(() => status.Text = text);
            await MachineSetup.InstallModAsync(settings, show, cancellation.Token);
            await MachineSetup.InstallTailscaleAsync(show, cancellation.Token);
            if (host)
            {
                await MachineSetup.InstallServerAsync(settings, show, cancellation.Token);
                MachineSetup.EnsureConfig(settings);
                if (eula.IsChecked == true) MachineSetup.AcceptEula(settings);
            }
            Files.Write(Files.SettingsPath, settings);
            Refresh();
            status.Text = host && !MachineSetup.EulaAccepted(settings)
                ? "Paketler hazır. Sunucu koşullarını okuyup kabul et; ardından bir kez daha Hazırla'ya bas."
                : "Hazır. Tailscale'e ilk kez giriş yapacaksan kendi hesabını kullan. Artık bu pencereyi kapatabilirsin.";
        }
        catch (OperationCanceledException) { status.Text = "İndirme durdu. Pencereyi açıp tekrar denediğinde kaldığı yerden devam eder."; }
        catch (Exception ex) { status.Text = ex.Message; Files.Log("Setup: " + ex.Message); }
        finally { busy = false; install.IsEnabled = browse.IsEnabled = true; progress.Visibility = Visibility.Hidden; Refresh(); }
    }
}
