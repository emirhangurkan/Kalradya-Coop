using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KalradyaCoop;

public sealed class AppSettings
{
    public string ServerExe { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "BannerlordCoopServer", "BannerlordCoopServer.exe");
    public string DataDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer");
    public string GameDir { get; set; } = "";
    public string Store { get; set; } = "";
    public string EpicApp { get; set; } = "Chickadee";
    public bool HostMode { get; set; }
    public string LastHostAddress { get; set; } = "";
    public string LastHostPassword { get; set; } = "";
    public string LastHostInvite { get; set; } = "";
    public string InviteLink { get; set; } = "";
    public bool DisconnectOnStop { get; set; } = true;
    public bool PreventSleep { get; set; } = true;
    public string BannerSymbol { get; set; } = "swords";
    public string BannerColor { get; set; } = "gold";
    public string CampaignsRoot { get; set; } = "";
    public List<CampaignSlot> Campaigns { get; set; } = new();
}

public static class Files
{
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KalradyaCoop");
    public static string SettingsPath => Path.Combine(Home, "settings.json");
    public static string SessionPath => Path.Combine(Home, "session.json");
    public static string LogDir => Path.Combine(Home, "Logs");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public static T? Read<T>(string path) { try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); } catch { return default; } }
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
    public static AppSettings Settings()
    {
        var saved = Read<AppSettings>(SettingsPath);
        var settings = saved ?? new();
        MachineSetup.Discover(settings);
        if (saved == null) settings.HostMode = File.Exists(ServerConfig.PathFor(settings));
        return settings;
    }
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(LogDir); File.AppendAllText(Path.Combine(LogDir, "app-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine); } catch { }
    }
    public static bool IsInvite(string value) => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && (uri.Host == "tailscale.com" || uri.Host.EndsWith(".tailscale.com", StringComparison.OrdinalIgnoreCase));
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}

public sealed record ServerConfig(string Name, int Port, string Password, int Autosave)
{
    public static string PathFor(AppSettings settings) => Path.Combine(settings.DataDir, "server-config.json");
    public static ServerConfig Read(AppSettings settings)
    {
        if (!File.Exists(PathFor(settings))) return new("Arkadaslar", 4200, "", 5);
        var j = JsonNode.Parse(File.ReadAllText(PathFor(settings)), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        var name = j["saveName"]?.GetValue<string>() ?? "Arkadaslar";
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_-]{1,64}$")) throw new IOException("Kayıt adı güvenli biçimde okunamadı.");
        var port = j["port"]?.GetValue<int>() ?? 4200;
        if (port is < 1 or > 65535) throw new IOException("Sunucu portu geçersiz.");
        return new(name, port, j["password"]?.GetValue<string>() ?? "", j["autosaveMinutes"]?.GetValue<int>() ?? 5);
    }
    public static void Update(AppSettings settings, string password, int autosave)
    {
        if (password.Length > 128 || password.Contains('\n') || password.Contains('\r')) throw new IOException("Şifre en fazla 128 karakter olmalı.");
        if (autosave is < 1 or > 30) throw new IOException("Otomatik kayıt 1–30 dakika olmalı.");
        var path = PathFor(settings);
        var j = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })! : new JsonObject { ["saveName"] = "Arkadaslar", ["port"] = 4200, ["steam"] = false };
        j["password"] = password;
        j["autosaveMinutes"] = autosave;
        var backup = Path.Combine(Files.Home, "ConfigBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (File.Exists(path)) File.Copy(path, backup);
        Files.Write(path, j);
    }
}

public sealed class Session
{
    public string RunToken { get; set; } = "";
    public string DataDir { get; set; } = "";
    public string ServerExe { get; set; } = "";
    public int WorkerPid { get; set; }
    public string WorkerExe { get; set; } = "";
    public DateTime WorkerStartedUtc { get; set; }
    public int ServerPid { get; set; }
    public DateTime ServerStartedUtc { get; set; }
    public string Error { get; set; } = "";
    public string Phase { get; set; } = "starting";
    public string LogPath { get; set; } = "";
    public int? ExitCode { get; set; }
}

public sealed class Endpoint
{
    public int Version { get; set; }
    public int Pid { get; set; }
    public string PipeName { get; set; } = "";
    public string Role { get; set; } = "";
    public string? PlatformId { get; set; }
    public string RunToken { get; set; } = "";
    public DateTime ProcessStartedUtc { get; set; }
}

public sealed record SaveStamp(long SavTicks, long JsonTicks, long SavBytes, long JsonBytes)
{
    public static SaveStamp Read(AppSettings settings, string name)
    {
        var folder = Path.Combine(settings.DataDir, "Game Saves");
        var sav = new FileInfo(Path.Combine(folder, name + ".sav"));
        var json = new FileInfo(Path.Combine(folder, name + ".json"));
        return new(sav.Exists ? sav.LastWriteTimeUtc.Ticks : 0, json.Exists ? json.LastWriteTimeUtc.Ticks : 0, sav.Exists ? sav.Length : 0, json.Exists ? json.Length : 0);
    }
    public bool IsNewerThan(SaveStamp before) => SavBytes > 1024 && JsonBytes > 2 && SavTicks > before.SavTicks && JsonTicks > before.JsonTicks;
}

public sealed record NetPeer(string Ip, bool Online, bool Active);
public sealed record NetworkState(bool Connected, string Ip, List<NetPeer> Peers, string Detail);
public sealed record AppSnapshot(bool Running, bool Ready, bool Managed, int? PlayersOnline, NetworkState Network, ServerConfig Config, SaveStamp Save, int SavedPlayers, string Error);
