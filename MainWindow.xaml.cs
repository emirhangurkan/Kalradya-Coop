using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace KalradyaCoop;

public partial class MainWindow : Window
{
    private readonly HostController controller;
    private readonly AppSettings settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly Forms.NotifyIcon tray;
    private AppSnapshot? snapshot;
    private bool busy, refreshing, exitRequested, trayHintShown;
    private DateTime lastNoticeUtc;
    private static readonly System.Windows.Media.Brush Green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(172, 195, 133));
    private static readonly System.Windows.Media.Brush Muted = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 175, 152));
    public MainWindow()
    {
        InitializeComponent();
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        WindowChrome.Install(this);
        settings = Files.Settings();
        controller = new(settings) { Progress = text => Dispatcher.Invoke(() => Tell(text)) };
        ApplyAppearance();
        ApplyMode();
        tray = new Forms.NotifyIcon { Text = "Kalradya Co-op", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Kalradya'yı aç", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Oyunu aç", null, (_, _) => Dispatcher.Invoke(() => _ = RunAsync(() => { controller.LaunchGame(); return Task.CompletedTask; })));
        menu.Items.Add("Kaydet ve kapat", null, (_, _) => Dispatcher.Invoke(() => _ = RunAsync(async () => await controller.StopAsync())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Programdan çık", null, (_, _) => Dispatcher.Invoke(() => _ = ExitAsync()));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { await RefreshAsync(); timer.Start(); };
        Closing += ClosingHandler;
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); };
    }
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Tell(string text, bool error = false)
    {
        Notice.Text = text;
        Notice.Foreground = error ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 171, 174)) : Muted;
        lastNoticeUtc = DateTime.UtcNow;
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (busy) return;
        busy = true; BusyBar.Visibility = Visibility.Visible; Buttons();
        try { await operation(); }
        catch (Exception ex) { Tell(ex.Message, true); Files.Log("Action error: " + ex.Message); }
        finally { busy = false; BusyBar.Visibility = Visibility.Hidden; await RefreshAsync(); Buttons(); }
    }
    private void Buttons()
    {
        PlayButton.IsEnabled = StartButton.IsEnabled = RepairButton.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy && (snapshot?.Running != true || snapshot.Ready);
        StartButton.IsEnabled = !busy && snapshot?.Running != true;
        SaveButton.IsEnabled = StopButton.IsEnabled = !busy && snapshot is { Managed: true, Ready: true };
        ShareButton.IsEnabled = !busy && snapshot is { Ready: true, Network.Connected: true };
        PlayButton.Content = busy || snapshot is { Running: true, Ready: false } ? "Hazırlanıyor…" : snapshot?.Ready == true ? "Oyunu aç" : "Oyunu başlat";
        var handedOff = WorldTransfer.ReadState(settings).HandedOff;
        if (handedOff) PlayButton.IsEnabled = StartButton.IsEnabled = false;
        SetupButton.IsEnabled = HostModeButton.IsEnabled = JoinModeButton.IsEnabled = JoinPlayButton.IsEnabled = AppearanceButton.IsEnabled = PasteJoinButton.IsEnabled = !busy;
        ExportWorldButton.IsEnabled = !busy && snapshot is { Save.SavTicks: > 0 } && (snapshot.Running == false || snapshot.Managed);
        ImportWorldButton.IsEnabled = !busy && snapshot?.Running != true;
        NewGameButton.IsEnabled = PickGameButton.IsEnabled = DifficultyButton.IsEnabled = !busy && snapshot is { Running: false };
    }
    private async Task RefreshAsync()
    {
        if (refreshing || busy && snapshot != null) return;
        refreshing = true;
        try
        {
            snapshot = await controller.SnapshotAsync();
            var s = snapshot;
            ServerStatus.Text = s.Ready ? "● Sunucu açık" : s.Running ? "Sunucu hazırlanıyor…" : "Sunucu kapalı";
            ServerStatus.Foreground = s.Ready ? Green : Muted;
            NetworkStatus.Text = s.Network.Connected ? "● Tailscale bağlı" : "Tailscale kapalı · başlatınca bağlanır";
            NetworkStatus.Foreground = s.Network.Connected ? Green : Muted;
            PlayersStatus.Text = s.PlayersOnline.HasValue ? s.PlayersOnline + " kişi oyunda" : "";
            HeroTitle.Text = s.Ready ? "Oyun hazır. Arkadaşlarını çağır." : s.Running ? "Oyun hazırlanıyor…" : "Kaldığınız yerden devam edin.";
            HeroSubtitle.Text = Campaigns.Title(settings) + "  ·  " + s.SavedPlayers + " kayıtlı karakter";
            CampaignActions.Visibility = s.Running ? Visibility.Collapsed : Visibility.Visible;
            HeroSubtitle.Visibility = s.Ready ? Visibility.Collapsed : Visibility.Visible;
            HostHint.Text = s.Ready ? "Bu bilgisayar açık kaldıkça arkadaşların oynayabilir." : "Sunucu hazırlanır, ardından oyun açılır.";
            ActiveHostPanel.Visibility = s.Ready ? Visibility.Visible : Visibility.Collapsed;
            StopButton.Visibility = s.Managed && s.Running ? Visibility.Visible : Visibility.Collapsed;
            if (!s.Running && s.Save.SavTicks == 0) { HeroTitle.Text = "İlk oyununuzu açın."; HeroSubtitle.Text = "Yeni bir sefer başlayacak. Eski kaydı almak için Diğer işlemler'i aç."; }
            IpText.Text = s.Network.Ip.Length > 0 ? s.Network.Ip + ":" + s.Config.Port : "Tailscale bağlanınca görünür";
            PasswordText.Text = s.Config.Password.Length > 0 ? s.Config.Password : "Şifre yok";
            LocalIpText.Text = "127.0.0.1:" + s.Config.Port;
            InviteButton.Content = Files.IsInvite(settings.InviteLink) ? "✓ Tailscale daveti" : "Tailscale daveti ekle";
            SaveTime.Text = s.Save.SavTicks > 0 ? "Son kayıt: " + new DateTime(s.Save.SavTicks, DateTimeKind.Utc).ToLocalTime().ToString("dd MMMM · HH:mm", new System.Globalization.CultureInfo("tr-TR")) : "İlk kayıt oyun başlayınca oluşacak.";
            SaveDetail.Text = "Oyun açıkken " + s.Config.Autosave + " dakikada bir kaydedilir.";
            if (WorldTransfer.ReadState(settings).HandedOff) { HeroTitle.Text = "Kayıt artık arkadaşında."; HeroSubtitle.Text = "Tekrar sen açacaksan: Diğer işlemler → Arkadaşımdan gelen kaydı aç."; }
            if (!settings.HostMode) { ServerStatus.Text = "Oyunu arkadaşın açacak"; PlayersStatus.Text = ""; }
            CompatibilityText.Text = MachineSetup.IsGame(settings.GameDir) ? "Bannerlord " + MachineSetup.GameVersion(settings.GameDir).TrimStart('v') + " · Coop " + (MachineSetup.ModInstalled(settings) ? MachineSetup.ModVersion : "kurulumu gerekli") : "Oyun kurulumu gerekli";
            var folder = Path.Combine(settings.DataDir, "Backups");
            BackupsButton.Content = Directory.Exists(folder) ? "Yedekler (" + Directory.GetDirectories(folder).Length + ")" : "Yedekler";
            tray.Text = "Kalradya Co-op • " + (s.Ready ? "Sunucu açık" : s.Running ? "Yükleniyor" : "Sunucu kapalı");
            if (!busy && DateTime.UtcNow - lastNoticeUtc > TimeSpan.FromSeconds(20))
            {
                Notice.Text = s.Running && !s.Managed ? "Sunucu başka bir uygulamadan açılmış." : s.Error;
                Notice.Foreground = Muted;
            }
            Buttons();
        }
        catch (Exception ex) { Tell(ex.Message, true); }
        finally { refreshing = false; }
    }
    private async void Play_Click(object sender, RoutedEventArgs e) { if (!PrepareHost()) return; await RunAsync(async () => { await controller.StartAsync(); controller.LaunchGame(); Tell("Başlatıcıda Play'e bas. Oyunda Join Co-op Sandbox → Direct'i aç; aşağıdaki adresini kullan."); }); }
    private async void NewGame_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            Campaigns.RequireStopped(controller);
            if (new CampaignWindow(settings, controller, true) { Owner = this }.ShowDialog() != true) return;
            await RefreshAsync();
            if (PrepareHost()) await RunAsync(async () => { await controller.StartAsync(); controller.LaunchGame(); Tell("Yeni sefer hazır. Oyunda Join Co-op Sandbox → Direct ile katıl."); });
        }
        catch (Exception ex) { Tell(ex.Message, true); }
    }
    private async void PickGame_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            Campaigns.RequireStopped(controller);
            var picker = new CampaignPicker(settings) { Owner = this };
            if (picker.ShowDialog() == true && picker.Selected is { } slot)
                await RunAsync(() => { Campaigns.Select(settings, slot, controller); Tell("Kayıt seçildi. Oyunu başlat'a bas."); return Task.CompletedTask; });
        }
        catch (Exception ex) { Tell(ex.Message, true); }
    }
    private async void Difficulty_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            Campaigns.RequireStopped(controller);
            if (new CampaignWindow(settings, controller, false) { Owner = this }.ShowDialog() == true)
            { Tell("Oyun ayarları kaydedildi. Bir sonraki açılışta herkes için uygulanacak."); await RefreshAsync(); }
        }
        catch (Exception ex) { Tell(ex.Message, true); }
    }
    private async void Start_Click(object sender, RoutedEventArgs e) { if (PrepareHost()) await RunAsync(() => controller.StartAsync()); }
    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => await controller.SaveAsync());
    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => await controller.StopAsync());
    private void Copy(string text, string label) { try { System.Windows.Clipboard.SetText(text); Tell(label + " kopyalandı."); } catch { Tell("Pano meşgul; yeniden dene.", true); } }
    private void CopyIp_Click(object sender, RoutedEventArgs e) { if (snapshot?.Network.Connected == true) Copy(IpText.Text, "Arkadaşlarının adresi"); else Tell("Önce Tailscale'i bağla.", true); }
    private void CopyPassword_Click(object sender, RoutedEventArgs e) { if (snapshot != null) Copy(snapshot.Config.Password, "Oyun şifresi"); }
    private void CopyLocal_Click(object sender, RoutedEventArgs e) => Copy(LocalIpText.Text, "Senin adresin");
    private void Share_Click(object sender, RoutedEventArgs e) { try { if (snapshot != null) Copy(controller.ShareText(snapshot), "Katılım bilgileri" + (Files.IsInvite(settings.InviteLink) ? " ve davet" : " (davet linkini ayrıca ekleyebilirsin)")); } catch (Exception ex) { Tell(ex.Message, true); } }
    private void Invite_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private async void OpenSettings() { if (busy) return; try { new SettingsWindow(settings, controller.ManagedRunning || controller.OtherServerRunning()) { Owner = this }.ShowDialog(); await RefreshAsync(); } catch (Exception ex) { Tell(ex.Message, true); } }
    private void OpenFolder(string folder) { Directory.CreateDirectory(folder); Files.Open(folder); }
    private void SavesFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(settings.DataDir, "Game Saves"));
    private void Backups_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(settings.DataDir, "Backups"));
    private void Logs_Click(object sender, RoutedEventArgs e) => OpenFolder(Files.LogDir);
    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        Tell("Tailscale ve oyun izni kontrol ediliyor…");
        var state = await NetworkService.ConnectAsync();
        var cfg = ServerConfig.Read(settings);
        if (settings.HostMode && File.Exists(settings.ServerExe) && !await NetworkService.CheckFirewallAsync(settings, state.Ip, cfg.Port)) { Tell("Ağ izni için Windows yönetici onayı gerekiyor…"); await NetworkService.RepairFirewallAsync(settings, state.Ip, cfg.Port); }
        Tell("Ağ izni hazır. Tailscale'de " + state.Peers.Count(x => x.Online) + " arkadaş cihazı çevrimiçi.");
    });
    private void ApplyMode()
    {
        HostPanel.Visibility = SaveCard.Visibility = HostTools.Visibility = settings.HostMode ? Visibility.Visible : Visibility.Collapsed;
        JoinPanel.Visibility = settings.HostMode ? Visibility.Collapsed : Visibility.Visible;
        ApplyRoleColors();
        JoinAddress.Text = settings.LastHostAddress; JoinPassword.Text = settings.LastHostPassword;
        JoinFields.IsExpanded = settings.LastHostAddress.Length > 0;
        AcceptInviteButton.Visibility = Files.IsInvite(settings.LastHostInvite) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplyRoleColors()
    {
        HostModeButton.Background = (System.Windows.Media.Brush)FindResource(settings.HostMode ? "Gold" : "Panel");
        HostModeButton.Foreground = (System.Windows.Media.Brush)FindResource(settings.HostMode ? "Bg" : "Ink");
        JoinModeButton.Background = (System.Windows.Media.Brush)FindResource(settings.HostMode ? "Panel" : "Gold");
        JoinModeButton.Foreground = (System.Windows.Media.Brush)FindResource(settings.HostMode ? "Ink" : "Bg");
    }
    private void ApplyAppearance()
    {
        Appearance.Apply(settings.BannerColor);
        BannerImage.Source = Appearance.Emblem(settings.BannerSymbol, settings.BannerColor);
        ApplyRoleColors();
    }
    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (new AppearanceWindow(settings) { Owner = this }.ShowDialog() == true) ApplyAppearance();
    }
    private async void HostMode_Click(object sender, RoutedEventArgs e) { if (busy) return; settings.HostMode = true; Files.Write(Files.SettingsPath, settings); ApplyMode(); await RefreshAsync(); }
    private async void JoinMode_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (controller.ManagedRunning) { Tell("Önce kendi sunucunu Kaydet ve kapat ile bitir."); return; }
        settings.HostMode = false; Files.Write(Files.SettingsPath, settings); ApplyMode(); await RefreshAsync();
    }
    private async void Setup_Click(object sender, RoutedEventArgs e) { if (busy) return; if (controller.ManagedRunning || controller.OtherServerRunning()) { Tell("Kurulum değişikliği için önce sunucuyu kapat."); return; } new SetupWindow(settings, settings.HostMode) { Owner = this }.ShowDialog(); await RefreshAsync(); }
    private bool PrepareHost()
    {
        if (MachineSetup.IsGame(settings.GameDir) && !MachineSetup.CompatibleGame(settings.GameDir)) { Tell("Bu paket Bannerlord 1.4.8 için. Önce oyun sürümünü arkadaşlarınla eşitle.", true); return false; }
        if (MachineSetup.IsGame(settings.GameDir) && MachineSetup.ModInstalled(settings) && File.Exists(settings.ServerExe) && File.Exists(NetworkService.TailscaleExe) && MachineSetup.EulaAccepted(settings)) { MachineSetup.EnsureConfig(settings); return true; }
        new SetupWindow(settings, true) { Owner = this }.ShowDialog();
        Tell("Kurulum tamamlanınca Oyunu başlat'a bas.");
        return false;
    }
    private void PasteJoin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var details = JoinDetails.Parse(System.Windows.Clipboard.GetText());
            settings.LastHostAddress = details.Address; settings.LastHostPassword = details.Password; settings.LastHostInvite = details.Invite;
            Files.Write(Files.SettingsPath, settings); ApplyMode(); Tell(Files.IsInvite(details.Invite) ? "Bilgiler geldi. İlk kez katılıyorsan daveti kabul et, sonra Oyunu aç'a bas." : "IP ve şifre geldi. Şimdi Oyunu aç'a bas.");
        }
        catch (Exception ex) { Tell(ex.Message, true); }
    }
    private void AcceptInvite_Click(object sender, RoutedEventArgs e) { if (Files.IsInvite(settings.LastHostInvite)) Files.Open(settings.LastHostInvite); }
    private void CopyJoinAddress_Click(object sender, RoutedEventArgs e) => Copy(JoinAddress.Text, "Host adresi");
    private void CopyJoinPassword_Click(object sender, RoutedEventArgs e) => Copy(JoinPassword.Text, "Şifre");
    private async void JoinPlay_Click(object sender, RoutedEventArgs e)
    {
        if (MachineSetup.IsGame(settings.GameDir) && !MachineSetup.CompatibleGame(settings.GameDir)) { Tell("Bu paket Bannerlord 1.4.8 için. Önce oyun sürümünü arkadaşlarınla eşitle.", true); return; }
        if (!MachineSetup.IsGame(settings.GameDir) || !MachineSetup.ModInstalled(settings) || !File.Exists(NetworkService.TailscaleExe)) { new SetupWindow(settings, false) { Owner = this }.ShowDialog(); return; }
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(JoinAddress.Text)) { JoinFields.IsExpanded = true; JoinAddress.Focus(); throw new IOException("Önce arkadaşının mesajını yapıştır veya IP adresini yaz."); }
            JoinDetails.ValidateAddress(JoinAddress.Text);
            settings.LastHostAddress = JoinAddress.Text.Trim(); settings.LastHostPassword = JoinPassword.Text;
            Files.Write(Files.SettingsPath, settings);
            Tell("Tailscale bağlanıyor…"); var network = await NetworkService.ConnectAsync();
            JoinDetails.CheckHostPresence(settings.LastHostAddress, network);
            controller.LaunchGame(); Copy(settings.LastHostAddress, "Host adresi"); Tell("Oyun açılıyor. Join Co-op → Direct ekranına adresi yapıştır.");
        });
    }
    private async void ExportWorld_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Yeni hosta göndereceğin dünya paketi", Filter = "Kalradya dünya paketi|*.kalradya", FileName = ServerConfig.Read(settings).Name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".kalradya", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(async () =>
        {
            if (controller.ManagedRunning) await controller.StopAsync();
            WorldTransfer.Export(settings, dialog.FileName);
            Tell("Kaydın hazır. Bu dosyayı arkadaşına gönder; Diğer işlemler → Arkadaşımdan gelen kaydı aç'ı seçsin.");
            Files.Open(Path.GetDirectoryName(dialog.FileName)!);
        });
    }
    private async void ImportWorld_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Son hostun gönderdiği dünya paketini seç", Filter = "Kalradya dünya paketi|*.kalradya" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(() =>
        {
            var info = WorldTransfer.Inspect(dialog.FileName);
            if (System.Windows.MessageBox.Show(this, info.SaveName + " dünyası bu bilgisayara alınacak. Mevcut aynı adlı kayıt önce yedeklenecek.\n\nDevam edilsin mi?", "Dünya al", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return Task.CompletedTask;
            WorldTransfer.Import(settings, dialog.FileName); Tell("Kayıt alındı. Oyunu başlat'a basarak devam edebilirsin."); return Task.CompletedTask;
        });
    }
    private void ClosingHandler(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && (controller.ManagedRunning || busy))
        {
            e.Cancel = true; Hide();
            if (!trayHintShown) { tray.ShowBalloonTip(3500, "Kalradya Co-op", "Sunucu arka planda çalışıyor. Kapatmak için Kaydet ve kapat'ı kullan.", Forms.ToolTipIcon.Info); trayHintShown = true; }
        }
    }
    private async Task ExitAsync()
    {
        if (busy) { ShowWindow(); Tell("Önce devam eden işlemin bitmesini bekle."); return; }
        if (controller.ManagedRunning)
        {
            var choice = System.Windows.MessageBox.Show(this, "Sunucuyu kaydedip kapatalım mı?\n\nEvet: kaydet ve kapat\nHayır: sunucu arka planda açık kalsın", "Kalradya", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.Yes) { await RunAsync(async () => await controller.StopAsync()); if (controller.ManagedRunning) return; }
        }
        exitRequested = true; Close();
    }
}
