using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KalradyaCoop;
public static class MachineSetup
{
    public const string ModVersion = "0.1.6";
    public const string ModUrl = "https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/download/v0.1.6/BannerlordCoop-v0.1.6.zip";
    public const string ModHash = "cc6199de88b91b47f81959abe978ea32296600774189d50b2b7273d90810a341";
    public const string CoreHash = "7cd7f77ccfe88cb482a2d13fcea593e17c7b9ef05d3cf449d844d38ba5d2a587";
    public const string ServerUrl = "https://pub-c80efa34191141fd803f8508e025f726.r2.dev/release/v0.1.6/BannerlordCoop-DedicatedServer-Win64-v0.1.6.7z";
    public const string ServerHash = "d1b36ad7ceb8a404ae97088fd57043cef313d597c92ef040c2747e2d1970d809";
    public const string TailscaleUrl = "https://pkgs.tailscale.com/stable/tailscale-setup-1.104.2-amd64.msi";
    // The official server fingerprints the combined, normalized EULA + LICENSE.
    private const string TermsHash = "a3d1998b757feded9739a5c6127be51204c0942a92b601703da8081ccc42213d";
    private const string EulaFileHash = "c402871c8bb3a688321eb6bfc904966963c92510de2cb75558e9c873e245ed65";
    private const string LicenseFileHash = "b2c2feee25d017798b0c1d60b7bb583a7b69b489ff240be62a40c6753591d694";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    public static string Configs => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs");
    public static bool IsGame(string path) => !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "Modules", "Native", "SubModule.xml"));
    public static string GameVersion(string path)
    {
        try { return XDocument.Load(Path.Combine(path, "Modules", "Native", "SubModule.xml")).Descendants("Version").First().Attribute("value")?.Value ?? ""; } catch { return ""; }
    }
    public static bool CompatibleGame(string path)
    {
        var value = GameVersion(path).TrimStart('v', 'V');
        return Version.TryParse(value, out var version) && version.Major == 1 && version.Minor == 4 && version.Build == 8;
    }
    public static void Discover(AppSettings settings)
    {
        var candidates = new List<(string Path, string Store, string App)>();
        try
        {
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests)) foreach (var file in Directory.GetFiles(manifests, "*.item"))
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    var root = json.RootElement;
                    var app = root.GetProperty("AppName").GetString() ?? "";
                    var path = root.GetProperty("InstallLocation").GetString() ?? "";
                    if (app == "Chickadee" || IsGame(path)) candidates.Add((path, "Epic", app));
                }
                catch { }
            }
        }
        catch { }
        try
        {
            using var registry = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steam = registry?.GetValue("SteamPath") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            var libraries = new List<string> { steam };
            var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile)) foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s+\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(library, "steamapps", "appmanifest_261550.acf");
                var dir = "Mount & Blade II Bannerlord";
                if (File.Exists(manifest))
                {
                    var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (match.Success && !match.Groups[1].Value.Contains("..")) dir = match.Groups[1].Value;
                }
                candidates.Add((Path.Combine(library, "steamapps", "common", dir), "Steam", ""));
            }
        }
        catch { }
        if (IsGame(settings.GameDir))
        {
            var same = candidates.FirstOrDefault(c => string.Equals(Path.GetFullPath(c.Path), Path.GetFullPath(settings.GameDir), StringComparison.OrdinalIgnoreCase));
            if (same.Path != null) { settings.Store = same.Store; settings.EpicApp = same.App; }
        }
        else
        {
            var found = candidates.FirstOrDefault(c => IsGame(c.Path));
            if (found.Path != null) { settings.GameDir = found.Path; settings.Store = found.Store; settings.EpicApp = found.App; }
        }
        if (!File.Exists(settings.ServerExe))
        {
            var installed = Path.Combine(Files.Home, "Server", "BannerlordCoopServer.exe");
            settings.ServerExe = installed;
        }
    }
    public static bool ModInstalled(AppSettings settings)
    {
        try
        {
            var core = Path.Combine(settings.GameDir, "Modules", "Coop", "bin", "Win64_Shipping_Client", "Coop.Core.dll");
            return Hash(core).Equals(CoreHash, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    public static bool EulaAccepted(AppSettings settings)
    {
        var file = Path.Combine(settings.DataDir, "eula.txt");
        if (!File.Exists(file)) return false;
        var eula = Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "EULA.md");
        return File.Exists(eula) && Hash(eula) == EulaFileHash && File.ReadAllLines(file).Any(x => x.Trim() == "eula=true")
            && File.ReadAllLines(file).Any(x => x.Trim().Equals("eula-sha256=" + TermsHash, StringComparison.OrdinalIgnoreCase));
    }
    public static void AcceptEula(AppSettings settings)
    {
        var file = Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "EULA.md");
        if (!File.Exists(file)) throw new IOException("Önce sunucu paketini indir.");
        if (Hash(file) != EulaFileHash || Hash(Path.Combine(Path.GetDirectoryName(file)!, "LICENSE.txt")) != LicenseFileHash) throw new IOException("Sunucu koşulları bu sürümle uyuşmuyor; kabul kaydedilmedi.");
        Directory.CreateDirectory(settings.DataDir);
        File.WriteAllText(Path.Combine(settings.DataDir, "eula.txt"), "# Accepted in Kalradya setup by this computer's user.\neula-sha256=" + TermsHash + "\neula=true\n");
    }
    public static void EnsureConfig(AppSettings settings)
    {
        if (File.Exists(ServerConfig.PathFor(settings))) return;
        ServerConfig.Update(settings, "Kalradya" + RandomNumberGenerator.GetInt32(1000, 9999), 5);
    }
    public static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static async Task<string> DownloadAsync(string url, string fileName, string? hash, Action<string> progress, CancellationToken cancellation)
    {
        var folder = Path.Combine(Files.Home, "Downloads"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path) && (hash == null || await Task.Run(() => Hash(path), cancellation) == hash)) return path;
        // Reuse an official archive that was downloaded during the original setup.
        var cached = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "BannerlordCoop-v0.1.6", fileName);
        if (hash != null && File.Exists(cached))
        {
            progress("Mevcut paket doğrulanıyor…");
            if (await Task.Run(() => Hash(cached), cancellation) == hash) return cached;
        }
        var partial = path + ".partial";
        long previous = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (previous > 0) request.Headers.Range = new(previous, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(partial); return await DownloadAsync(url, fileName, hash, progress, cancellation); }
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent) previous = 0;
        var total = (response.Content.Headers.ContentLength ?? 0) + previous;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
        await using (var output = new FileStream(partial, previous > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var buffer = new byte[1024 * 1024]; var received = previous; var next = DateTime.MinValue;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellation); if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation); received += count;
                if (DateTime.UtcNow > next) { progress(total > 0 ? $"İndiriliyor · %{received * 100 / total} · {received / 1048576:N0} MB" : $"İndiriliyor · {received / 1048576:N0} MB"); next = DateTime.UtcNow.AddMilliseconds(500); }
            }
        }
        progress("İndirilen paket doğrulanıyor…");
        if (hash != null && await Task.Run(() => Hash(partial), cancellation) != hash) { File.Delete(partial); throw new IOException("Paket doğrulaması başarısız; kurulum yapılmadı."); }
        File.Move(partial, path, true);
        return path;
    }
    public static async Task InstallTailscaleAsync(Action<string> progress, CancellationToken cancellation)
    {
        if (File.Exists(NetworkService.TailscaleExe)) return;
        var msi = await DownloadAsync(TailscaleUrl, "tailscale-setup-1.104.2-amd64.msi", null, progress, cancellation);
        var script = "$s=Get-AuthenticodeSignature -LiteralPath '" + msi.Replace("'", "''") + "'; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Tailscale'){exit 7}";
        var signature = await NetworkService.RunAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }, 40);
        if (signature.ExitCode != 0) throw new IOException("Tailscale kurulum imzası doğrulanamadı.");
        progress("Tailscale kuruluyor; Windows yönetici onayı isteyebilir…");
        using var installer = Process.Start(new ProcessStartInfo("msiexec.exe") { Arguments = "/i \"" + msi + "\" /passive /norestart", UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })!;
        await installer.WaitForExitAsync();
        if (installer.ExitCode is not (0 or 3010) || !File.Exists(NetworkService.TailscaleExe)) throw new IOException("Tailscale kurulumu tamamlanmadı.");
    }
    public static async Task InstallServerAsync(AppSettings settings, Action<string> progress, CancellationToken cancellation)
    {
        if (File.Exists(settings.ServerExe)) return;
        var archive = await DownloadAsync(ServerUrl, "BannerlordCoop-DedicatedServer-Win64-v0.1.6.7z", ServerHash, progress, cancellation);
        var destination = Path.GetDirectoryName(settings.ServerExe)!;
        var stage = destination + ".install-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        progress("Sunucu hazırlanıyor… bu işlem birkaç dakika sürebilir.");
        var tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        if (!File.Exists(tar)) throw new IOException("Bu Windows sürümünde tar.exe bulunamadı. Windows 10/11 güncel sürümü gerekli.");
        var extracted = await NetworkService.RunAsync(tar, new[] { "-xf", archive, "-C", stage }, 1800);
        if (extracted.ExitCode != 0 || !File.Exists(Path.Combine(stage, "BannerlordCoopServer.exe"))) throw new IOException("Sunucu paketi açılamadı. " + extracted.Error);
        cancellation.ThrowIfCancellationRequested();
        if (Directory.Exists(destination)) Directory.Move(destination, destination + ".previous-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.Move(stage, destination);
        EnsureConfig(settings);
    }
    public static async Task InstallModAsync(AppSettings settings, Action<string> progress, CancellationToken cancellation)
    {
        if (!IsGame(settings.GameDir)) throw new IOException("Önce yüklü Bannerlord klasörünü seç.");
        if (!CompatibleGame(settings.GameDir)) throw new IOException("Bu paket Bannerlord 1.4.8 ve Coop 0.1.6 için hazırlandı. Oyun sürümünü arkadaşlarınla eşitle.");
        if (!ModInstalled(settings))
        {
            var zip = await DownloadAsync(ModUrl, "BannerlordCoop-v0.1.6.zip", ModHash, progress, cancellation);
            var jobFile = Path.Combine(Files.Home, "mod-install-" + Guid.NewGuid().ToString("N") + ".json");
            Files.Write(jobFile, new ModInstallJob(settings.GameDir, zip, jobFile + ".result"));
            progress("Coop kuruluyor; Windows yönetici onayı isteyebilir…");
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "--install-mod \"" + jobFile + "\"", UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })!;
            await child.WaitForExitAsync();
            if (child.ExitCode != 0 || !ModInstalled(settings)) throw new IOException("Coop kurulumu tamamlanmadı. " + (File.Exists(jobFile + ".result") ? File.ReadAllText(jobFile + ".result") : ""));
        }
        ConfigureLauncher(settings);
    }
    public static int InstallModElevated(string jobFile)
    {
        ModInstallJob? job = null;
        try
        {
            job = Files.Read<ModInstallJob>(jobFile) ?? throw new IOException("Kurulum bilgisi okunamadı.");
            if (!IsGame(job.GameDir) || Hash(job.ZipPath) != ModHash) throw new IOException("Oyun veya Coop paketi doğrulanamadı.");
            if (Process.GetProcessesByName("Bannerlord").Length > 0 || Process.GetProcessesByName("TaleWorlds.MountAndBlade.Launcher").Length > 0) throw new IOException("Önce Bannerlord ve başlatıcısını kapat.");
            var stage = Path.Combine(job.GameDir, ".kalradya-mod-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(job.ZipPath, stage);
            var source = Path.Combine(stage, "Coop");
            if (!File.Exists(Path.Combine(source, "SubModule.xml"))) throw new IOException("Coop paketi geçersiz.");
            var destination = Path.Combine(job.GameDir, "Modules", "Coop");
            string? backup = null;
            if (Directory.Exists(destination))
            {
                backup = Path.Combine(job.GameDir, "KalradyaModBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!); Directory.Move(destination, backup);
            }
            try { Directory.Move(source, destination); }
            catch { if (backup != null && !Directory.Exists(destination)) Directory.Move(backup, destination); throw; }
            File.WriteAllText(job.ResultPath, "ok");
            return 0;
        }
        catch (Exception ex) { if (job != null) File.WriteAllText(job.ResultPath, ex.Message); return 1; }
    }
    public static void ConfigureLauncher(AppSettings settings, string? configDirectory = null)
    {
        var configRoot = configDirectory ?? Configs;
        var file = Path.Combine(configRoot, "LauncherData.xml");
        var doc = File.Exists(file) ? XDocument.Load(file) : new XDocument(new XElement("UserData", new XElement("GameType", "Singleplayer"), new XElement("SingleplayerData", new XElement("ModDatas"))));
        var root = doc.Root!;
        root.SetElementValue("GameType", "Singleplayer");
        var section = root.Element("SingleplayerData"); if (section == null) { section = new XElement("SingleplayerData"); root.Add(section); }
        var mods = section.Element("ModDatas"); if (mods == null) { mods = new XElement("ModDatas"); section.Add(mods); }
        foreach (var id in new[] { "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "Coop" })
        {
            var mod = mods.Elements("UserModData").FirstOrDefault(x => x.Element("Id")?.Value == id);
            if (mod == null) { mod = new XElement("UserModData", new XElement("Id", id)); mods.Add(mod); }
            mod.SetElementValue("IsSelected", "true");
            mod.SetElementValue("LastKnownVersion", id == "Coop" ? "v0.1.6.0" : GameVersion(settings.GameDir));
        }
        foreach (var mod in mods.Elements("UserModData").Where(x => new[] { "NavalDLC", "BirthAndDeath", "FastMode" }.Contains(x.Element("Id")?.Value))) mod.SetElementValue("IsSelected", "false");
        Directory.CreateDirectory(configRoot);
        if (File.Exists(file) && XNode.DeepEquals(XDocument.Load(file), doc)) return;
        if (File.Exists(file)) File.Copy(file, file + ".kalradya-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".bak");
        var temp = file + ".kalradya.tmp"; doc.Save(temp); File.Move(temp, file, true);
    }
}
public sealed record ModInstallJob(string GameDir, string ZipPath, string ResultPath);
