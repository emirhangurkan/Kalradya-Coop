using System.IO;
using System.Windows;

namespace KalradyaCoop;
public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly bool running;
    private readonly string oldPassword;
    private readonly int oldAutosave;
    public SettingsWindow(AppSettings settings, bool running)
    {
        InitializeComponent();
        WindowChrome.Install(this);
        this.settings = settings; this.running = running;
        var cfg = ServerConfig.Read(settings);
        oldPassword = cfg.Password; oldAutosave = cfg.Autosave;
        InviteText.Text = settings.InviteLink;
        GamePassword.Text = cfg.Password;
        Autosave.Text = cfg.Autosave.ToString();
        DisconnectBox.IsChecked = settings.DisconnectOnStop;
        PreventSleepBox.IsChecked = settings.PreventSleep;
        GamePassword.IsEnabled = Autosave.IsEnabled = !running;
        if (running) ConfigHint.Text = "Şifre ve kayıt aralığı için önce sunucuyu kapat.";
    }
    private void OpenShare_Click(object sender, RoutedEventArgs e) => Files.Open("https://login.tailscale.com/admin/machines");
    private void Paste_Click(object sender, RoutedEventArgs e) { try { InviteText.Text = System.Windows.Clipboard.GetText().Trim(); } catch { ErrorText.Text = "Pano okunamadı; yeniden dene."; } }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var invite = InviteText.Text.Trim();
            if (invite.Length > 0 && !Files.IsInvite(invite)) throw new IOException("Tailscale'in verdiği https bağlantısını yapıştır.");
            if (!int.TryParse(Autosave.Text, out var minutes) || minutes is < 1 or > 30) throw new IOException("Kayıt aralığı 1–30 dakika olmalı.");
            if (!running && (oldPassword != GamePassword.Text || oldAutosave != minutes)) ServerConfig.Update(settings, GamePassword.Text, minutes);
            settings.InviteLink = invite;
            settings.DisconnectOnStop = DisconnectBox.IsChecked == true;
            settings.PreventSleep = PreventSleepBox.IsChecked == true;
            Files.Write(Files.SettingsPath, settings);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
