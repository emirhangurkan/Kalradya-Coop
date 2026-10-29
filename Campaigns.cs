using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KalradyaCoop;

public sealed record CampaignSlot(string Title, string DataDir)
{
    public override string ToString() => Title;
}

public static class Campaigns
{
    public static readonly (string Key, string Label)[] DifficultyFields =
    [
        ("playerReceivedDamage", "Senin aldığın hasar"), ("playerTroopsReceivedDamage", "Askerlerinin aldığı hasar"),
        ("combatAIDifficulty", "Savaş yapay zekâsı"), ("recruitmentDifficulty", "Asker toplama"),
        ("playerMapMovementSpeed", "Harita hareketi"), ("stealthAndDisguiseDifficulty", "Gizlenme ve kılık değiştirme"),
        ("persuasionSuccessChance", "İkna"), ("clanMemberDeathChance", "Klan üyelerinin savaşta ölümü"),
        ("battleDeath", "Kahramanların savaşta ölümü")
    ];
    public static readonly string[] Levels = ["VeryEasy", "Easy", "Realistic"];
    public static JsonObject Parse(string text) => JsonNode.Parse(text, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new IOException("Oyun ayarları okunamadı.");
    public static List<CampaignSlot> List(AppSettings settings)
    {
        var list = (settings.Campaigns ?? new()).ToList();
        if (!list.Any(x => SamePath(x.DataDir, settings.DataDir))) list.Insert(0, new(ServerConfig.Read(settings).Name, settings.DataDir));
        return list;
    }
    private static bool SamePath(string a, string b) => Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    public static string Title(AppSettings settings) => List(settings).First(x => SamePath(x.DataDir, settings.DataDir)).Title;
    public static void RequireStopped(HostController controller)
    {
        if (controller.ManagedRunning || controller.OtherServerRunning()) throw new IOException("Önce açık oyunu Kaydet ve kapat ile bitir.");
    }
    public static JsonObject ReadOptions(AppSettings settings)
    {
        var local = Path.Combine(settings.DataDir, "mod-config.json");
        var inherited = Environment.GetEnvironmentVariable("COOP_DATA_DIR");
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "CoopData", "mod-config.json");
        var template = Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "engine", "Modules", "Coop", "mod-config.default.json");
        var path = new[] { local, string.IsNullOrWhiteSpace(inherited) ? "" : Path.Combine(inherited, "mod-config.json"), shared, template }.FirstOrDefault(File.Exists);
        if (path == null) throw new IOException("Önce İlk kurulum bölümünden sunucu paketini hazırla.");
        return Parse(File.ReadAllText(path));
    }
    public static void ValidateOptions(JsonObject options)
    {
        var difficulty = options["difficulty"] as JsonObject ?? throw new IOException("Zorluk seçenekleri eksik.");
        foreach (var field in DifficultyFields)
            if (!Levels.Contains(difficulty[field.Key]?.GetValue<string>())) throw new IOException("Geçersiz zorluk seçimi: " + field.Label);
        foreach (var key in new[] { "birthAndDeath", "autoAllocateClanMemberPerks" })
            if (difficulty[key] is not JsonValue value || !value.TryGetValue<bool>(out _)) throw new IOException("Geçersiz oyun seçeneği.");
        var size = options["modOptions"]?["battleSize"]?.GetValue<int>() ?? 1000;
        if (size is < 200 or > 1000) throw new IOException("Savaş büyüklüğü 200–1000 olmalı.");
    }
    public static void SaveOptions(AppSettings settings, JsonObject options, HostController controller)
    {
        RequireStopped(controller); ValidateOptions(options);
        var path = Path.Combine(settings.DataDir, "mod-config.json");
        Directory.CreateDirectory(settings.DataDir);
        var backup = Path.Combine(settings.DataDir, "OptionBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
        // Keep the effective previous options, even when migrating from shared options.
        Files.Write(backup, ReadOptions(settings));
        Files.Write(path, options);
    }
    public static CampaignSlot Create(AppSettings settings, string title, JsonObject options, HostController controller, string? settingsPath = null)
    {
        RequireStopped(controller); ValidateOptions(options);
        title = title.Trim();
        if (title.Length is < 1 or > 48 || title.Any(char.IsControl)) throw new IOException("Sefer adı 1–48 karakter olmalı.");
        var oldDir = settings.DataDir;
        var oldRoot = settings.CampaignsRoot;
        var oldList = settings.Campaigns;
        var root = string.IsNullOrWhiteSpace(oldRoot) ? Path.Combine(oldDir, "Seferler") : oldRoot;
        var id = "Sefer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var slot = new CampaignSlot(title, Path.Combine(root, id));
        Directory.CreateDirectory(slot.DataDir);
        var configPath = ServerConfig.PathFor(settings);
        var config = File.Exists(configPath) ? Parse(File.ReadAllText(configPath)) : new JsonObject { ["port"] = 4200, ["password"] = "Kalradya" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 9999), ["steam"] = false, ["autosaveMinutes"] = 5 };
        config["saveName"] = id;
        // Do not reuse a manually specified log file from the old campaign.
        config.Remove("logFile");
        Files.Write(Path.Combine(slot.DataDir, "server-config.json"), config);
        Files.Write(Path.Combine(slot.DataDir, "mod-config.json"), options);
        if (MachineSetup.EulaAccepted(settings)) File.Copy(Path.Combine(oldDir, "eula.txt"), Path.Combine(slot.DataDir, "eula.txt"));
        var list = List(settings); list.Add(slot);
        try
        {
            settings.DataDir = slot.DataDir; settings.CampaignsRoot = root; settings.Campaigns = list;
            Files.Write(settingsPath ?? Files.SettingsPath, settings);
        }
        catch { settings.DataDir = oldDir; settings.CampaignsRoot = oldRoot; settings.Campaigns = oldList; throw; }
        return slot;
    }
    public static void Select(AppSettings settings, CampaignSlot slot, HostController controller, string? settingsPath = null)
    {
        RequireStopped(controller);
        if (!List(settings).Contains(slot)) throw new IOException("Bu sefer kayıt listesinde yok.");
        var probe = new AppSettings { DataDir = slot.DataDir, ServerExe = settings.ServerExe };
        if (!File.Exists(ServerConfig.PathFor(probe))) throw new IOException("Bu seferin klasörü bulunamadı.");
        var name = ServerConfig.Read(probe).Name;
        var stamp = SaveStamp.Read(probe, name);
        if (stamp.SavTicks != 0 || stamp.JsonTicks != 0) new HostController(probe).ValidatePair(name);
        var oldDir = settings.DataDir;
        try { settings.DataDir = slot.DataDir; Files.Write(settingsPath ?? Files.SettingsPath, settings); }
        catch { settings.DataDir = oldDir; throw; }
    }
}
