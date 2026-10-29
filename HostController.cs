using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;

namespace KalradyaCoop;

public sealed class HostController
{
    public AppSettings Settings { get; }
    public string SessionFile { get; }
    public Action<string>? Progress { get; set; }
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly bool testMode;
    public HostController(AppSettings settings, string? sessionFile = null, bool testMode = false) { Settings = settings; SessionFile = sessionFile ?? Files.SessionPath; this.testMode = testMode; }
    public Session? Session => Files.Read<Session>(SessionFile);
    public bool ManagedRunning => Session is { } s && (PipeControl.ProcessMatches(s.ServerPid, s.ServerStartedUtc, s.ServerExe) || PipeControl.ProcessMatches(s.WorkerPid, s.WorkerStartedUtc, s.WorkerExe));
    public bool OtherServerRunning()
    {
        foreach (var p in Process.GetProcessesByName("BannerlordCoopServer"))
            using (p) { try { if (!p.HasExited && p.MainModule?.FileName?.Equals(Settings.ServerExe, StringComparison.OrdinalIgnoreCase) == true) return true; } catch { } }
        return false;
    }
    public async Task<AppSnapshot> SnapshotAsync()
    {
        var config = ServerConfig.Read(Settings);
        var running = ManagedRunning;
        var ready = false;
        int? connected = null;
        string error = "";
        var networkTask = NetworkService.ReadAsync();
        if (running && Session is { } liveSession)
        {
            try
            {
                var status = await PipeControl.SendAsync(liveSession, "status");
                ready = status.Ready;
                connected = status.Players;
            }
            catch { error = "Sunucu yanıtı bekleniyor"; }
        }
        var savedPlayers = 0;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Settings.DataDir, "Game Saves", config.Name + ".json"))); savedPlayers = doc.RootElement.GetProperty("Players").GetArrayLength(); } catch { }
        if (!running && Session?.Phase == "error") error = Session.Error;
        return new(running || OtherServerRunning(), ready, running, connected, await networkTask, config, SaveStamp.Read(Settings, config.Name), savedPlayers, error);
    }
    public async Task StartAsync(bool connectNetwork = true)
    {
        await operations.WaitAsync();
        try
        {
            var cfg = ServerConfig.Read(Settings);
            if (!File.Exists(Settings.ServerExe)) throw new IOException("Sunucu programı bulunamadı.");
            if (WorldTransfer.ReadState(Settings).HandedOff) throw new IOException("Bu dünya devredildi. Yeniden host olmak için son ev sahibinden güncel dünya paketini al.");
            if (!MachineSetup.EulaAccepted(Settings)) throw new IOException("Kurulum ekranında sunucu kullanım koşullarını inceleyip kabul et.");
            if (connectNetwork)
            {
                Progress?.Invoke("Tailscale bağlanıyor…");
                var network = await NetworkService.ConnectAsync();
                Progress?.Invoke("Bağlantı izni kontrol ediliyor…");
                if (!await NetworkService.CheckFirewallAsync(Settings, network.Ip, cfg.Port))
                {
                    Progress?.Invoke("Oyun bağlantısı için Windows yönetici onayını bekliyorum…");
                    await NetworkService.RepairFirewallAsync(Settings, network.Ip, cfg.Port);
                }
            }
            if (!ManagedRunning)
            {
                if (OtherServerRunning()) throw new IOException("Sunucu başka bir pencerede açık. Önce onu kapatıp bu programdan başlat.");
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(x => x.Port == cfg.Port)) throw new IOException("Oyun portunu başka bir program kullanıyor.");
                Progress?.Invoke("Mevcut kayıt yedekleniyor…");
                var pair = SaveStamp.Read(Settings, cfg.Name);
                if (pair.SavTicks > 0 || pair.JsonTicks > 0) Backup(cfg.Name, "Baslamadan");
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var launch = new WorkerLaunch { Settings = Settings, SessionPath = SessionFile, LogPath = Path.Combine(Files.LogDir, "server-" + stamp + ".log") };
                var launchPath = Path.Combine(Path.GetDirectoryName(SessionFile)!, "launch-" + stamp + ".json");
                Files.Write(launchPath, launch);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--worker"); start.ArgumentList.Add(launchPath);
                using var worker = Process.Start(start) ?? throw new IOException("Sunucu başlatılamadı.");
                for (var i = 0; i < 30 && !ManagedRunning; i++) { if (worker.HasExited) throw new IOException("Sunucu başlatıcısı kapandı. Günlükleri kontrol et."); await Task.Delay(500); }
            }
            Progress?.Invoke("Dünya yükleniyor… ilk açılış birkaç dakika sürebilir.");
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromMinutes(6))
            {
                var s = Session;
                if (s?.Phase is "error" or "stopped" || !ManagedRunning) throw new IOException(s?.Error is { Length: > 0 } ? s.Error : "Sunucu dünya yüklenirken kapandı.");
                if (s != null)
                {
                    try
                    {
                        var status = await PipeControl.SendAsync(s, "status");
                        if (status.Ready && IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(x => x.Port == cfg.Port)) { Progress?.Invoke("Sunucu hazır. Arkadaşların katılabilir."); return; }
                    }
                    catch (Exception ex) { Files.Log("Start probe: " + ex.Message); }
                }
                await Task.Delay(2500);
            }
            throw new IOException("Dünya yüklemesi henüz tamamlanmadı. Sunucu açık bırakıldı; biraz sonra yeniden dene.");
        }
        finally { operations.Release(); }
    }
    private Session RequireSession() => ManagedRunning && Session is { } s ? s : throw new IOException("Sunucu bu programdan başlatılmış olmalı.");
    public async Task<string> SaveAsync()
    {
        await operations.WaitAsync();
        try
        {
            var cfg = ServerConfig.Read(Settings);
            var s = RequireSession();
            Progress?.Invoke("Oyun kaydediliyor…");
            var before = SaveStamp.Read(Settings, cfg.Name);
            await PipeControl.SendAsync(s, "save");
            await WaitFreshPairAsync(cfg.Name, before);
            var backup = Backup(cfg.Name, "Kayit");
            Progress?.Invoke("Kayıt tamamlandı ve yedeklendi.");
            return backup;
        }
        finally { operations.Release(); }
    }
    private async Task WaitFreshPairAsync(string name, SaveStamp before)
    {
        for (var i = 0; i < 140; i++)
        {
            if (SaveStamp.Read(Settings, name).IsNewerThan(before))
            {
                var a = SaveStamp.Read(Settings, name);
                await Task.Delay(2000);
                if (a == SaveStamp.Read(Settings, name)) { ValidatePair(name); return; }
            }
            await Task.Delay(1000);
        }
        throw new IOException("Yeni kayıt doğrulanamadı. Sunucu kapatılmadı; yeniden Kaydet'i dene.");
    }
    public async Task<string> StopAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (!ManagedRunning)
            {
                if (OtherServerRunning()) throw new IOException("Bu sunucu başka bir programdan açılmış. Güvenli kayıt kontrolü için kendi penceresinden kapat.");
                return "";
            }
            var cfg = ServerConfig.Read(Settings);
            var s = RequireSession();
            var before = SaveStamp.Read(Settings, cfg.Name);
            Progress?.Invoke("Son kayıt alınıyor ve sunucu güvenle kapanıyor…");
            // Production 'stop' closes admissions, saves the paired files, and exits
            // only on success. Never terminate the process on a save error/timeout.
            await PipeControl.SendAsync(s, "stop");
            for (var i = 0; i < 180 && ManagedRunning; i++) await Task.Delay(1000);
            if (ManagedRunning) throw new IOException("Sunucu kapanışı tamamlanmadı; zorla kapatılmadı. Günlükleri kontrol et.");
            if (Session?.ExitCode != 0 || !SaveStamp.Read(Settings, cfg.Name).IsNewerThan(before)) throw new IOException("Sunucu kapandı ama son kayıt doğrulanamadı. Mevcut kayıt ve yedekler korunuyor; günlükleri kontrol et.");
            ValidatePair(cfg.Name);
            var backup = Backup(cfg.Name, "Oturum-sonu");
            if (!testMode && Settings.DisconnectOnStop) { Progress?.Invoke("Tailscale bağlantısı kesiliyor…"); await NetworkService.DisconnectAsync(); }
            Progress?.Invoke("Son kayıt yedeklendi. Sunucu kapalı.");
            return backup;
        }
        finally { operations.Release(); }
    }
    public void ValidatePair(string name)
    {
        var stamp = SaveStamp.Read(Settings, name);
        if (stamp.SavBytes < 1024 || stamp.JsonBytes < 2) throw new IOException("Kayıt dosyalarından biri eksik veya boş.");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Settings.DataDir, "Game Saves", name + ".json")));
        if (!json.RootElement.TryGetProperty("Players", out var players) || players.ValueKind != JsonValueKind.Array) throw new IOException("Oyuncu kayıt dosyası doğrulanamadı.");
    }
    public string Backup(string name, string reason)
    {
        ValidatePair(name);
        var source = Path.Combine(Settings.DataDir, "Game Saves");
        var folder = Path.Combine(Settings.DataDir, "Backups", reason + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
        Directory.CreateDirectory(folder);
        using var sav = new FileStream(Path.Combine(source, name + ".sav"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var json = new FileStream(Path.Combine(source, name + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (var pair in new[] { (stream: sav, ext: ".sav"), (stream: json, ext: ".json") })
        {
            var dest = Path.Combine(folder, name + pair.ext);
            using (var output = File.Create(dest)) pair.stream.CopyTo(output);
            pair.stream.Position = 0;
            var srcHash = SHA256.HashData(pair.stream);
            using var copied = File.OpenRead(dest);
            if (!srcHash.SequenceEqual(SHA256.HashData(copied))) throw new IOException("Yedek dosyası doğrulanamadı.");
        }
        Files.Write(Path.Combine(folder, "backup-info.json"), new { createdUtc = DateTime.UtcNow, save = name, reason });
        return folder;
    }
    public void LaunchGame()
    {
        if (!Directory.Exists(Settings.GameDir)) throw new IOException("Bannerlord kurulumu bulunamadı.");
        if (!MachineSetup.CompatibleGame(Settings.GameDir) || !MachineSetup.ModInstalled(Settings)) throw new IOException("Bannerlord 1.4.8 ve Coop 0.1.6 gerekli. Kurulum ekranını kontrol et.");
        // Refresh the selected modules before a cold launch. Never rewrite a live launcher's settings.
        bool gameOpen = false;
        foreach (var processName in new[] { "Bannerlord", "TaleWorlds.MountAndBlade.Launcher" })
            foreach (var process in Process.GetProcessesByName(processName)) using (process) { if (!process.HasExited) gameOpen = true; }
        if (!gameOpen) MachineSetup.ConfigureLauncher(Settings);
        if (Settings.Store == "Steam") Files.Open("steam://rungameid/261550");
        else if (Settings.Store == "Epic") Files.Open("com.epicgames.launcher://apps/" + Uri.EscapeDataString(Settings.EpicApp) + "?action=launch&silent=true");
        else
        {
            var launcher = Path.Combine(Settings.GameDir, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.Launcher.exe");
            if (!File.Exists(launcher)) throw new IOException("Oyun başlatıcısı bulunamadı; Kurulum'dan oyun klasörünü seç.");
            Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = Path.GetDirectoryName(launcher), UseShellExecute = true });
        }
    }
    public string ShareText(AppSnapshot state)
    {
        if (!state.Ready || !state.Network.Connected) throw new IOException("Önce sunucuyu başlat; paylaşılacak adres o zaman hazır olacak.");
        var invite = Files.IsInvite(Settings.InviteLink) ? "Tailscale daveti: " + Settings.InviteLink.Trim() + "\n" : "";
        return "Bannerlord Co-op\n" + invite + "Adres: " + state.Network.Ip + ":" + state.Config.Port + "\nŞifre: " + state.Config.Password + "\nKalradya Co-op: Arkadaşa katıl → Katılım mesajını yapıştır.\nOyunda Join Co-op Sandbox → Direct → Join.\nBannerlord 1.4.8 / Coop " + MachineSetup.ModVersion + ".";
    }
}
