using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KalradyaCoop;
public sealed record WorldState(string WorldId = "", long Generation = 0, bool HandedOff = false, string PackageId = "");
public sealed record WorldManifest(int Format, string WorldId, long Generation, string PackageId, string SaveName, string GameVersion, string CoopVersion, DateTime ExportedUtc, string SavSha256, string JsonSha256);

public static class WorldTransfer
{
    private static string StatePath(AppSettings settings) => Path.Combine(settings.DataDir, "kalradya-world.json");
    public static WorldState ReadState(AppSettings settings) => Files.Read<WorldState>(StatePath(settings)) ?? new();
    public static string Export(AppSettings settings, string destination)
    {
        var controller = new HostController(settings);
        if (controller.ManagedRunning || controller.OtherServerRunning()) throw new IOException("Dünyayı devretmeden önce sunucuyu kaydedip kapat.");
        var cfg = ServerConfig.Read(settings);
        var backup = controller.Backup(cfg.Name, "Devir");
        var state = ReadState(settings);
        var id = state.WorldId.Length > 0 ? state.WorldId : Guid.NewGuid().ToString("N");
        var generation = state.HandedOff ? state.Generation : state.Generation + 1;
        var packageId = state.HandedOff ? state.PackageId : Guid.NewGuid().ToString("N");
        var manifest = new WorldManifest(1, id, generation, packageId, cfg.Name, "1.4.8", MachineSetup.ModVersion, DateTime.UtcNow,
            MachineSetup.Hash(Path.Combine(backup, cfg.Name + ".sav")), MachineSetup.Hash(Path.Combine(backup, cfg.Name + ".json")));
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(backup, cfg.Name + ".sav"), "world.sav", CompressionLevel.Fastest);
            zip.CreateEntryFromFile(Path.Combine(backup, cfg.Name + ".json"), "world.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(manifest, Files.Json));
        }
        // Read the finished archive and verify both payload hashes before handing off.
        Inspect(temp);
        File.Move(temp, destination, true);
        Files.Write(StatePath(settings), new WorldState(id, generation, true, packageId));
        return destination;
    }
    public static WorldManifest Inspect(string package)
    {
        using var zip = ZipFile.OpenRead(package);
        if (zip.Entries.Count != 3 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != 3 || zip.Entries.Any(e => e.FullName is not ("world.sav" or "world.json" or "manifest.json"))) throw new IOException("Geçerli bir Kalradya dünya paketi seç.");
        if (zip.GetEntry("manifest.json")!.Length > 16384 || zip.GetEntry("world.sav")!.Length is < 1024 or > 1073741824 || zip.GetEntry("world.json")!.Length is < 2 or > 52428800) throw new IOException("Dünya paketinin dosya boyutları geçersiz.");
        using var meta = zip.GetEntry("manifest.json")!.Open();
        var manifest = JsonSerializer.Deserialize<WorldManifest>(meta, Files.Json) ?? throw new IOException("Dünya bilgisi okunamadı.");
        if (manifest.Format != 1 || !Guid.TryParse(manifest.WorldId, out _) || !Guid.TryParse(manifest.PackageId, out _) || manifest.Generation < 1 ||
            !Regex.IsMatch(manifest.SaveName, @"^[A-Za-z0-9_-]{1,64}$") || manifest.CoopVersion != MachineSetup.ModVersion || manifest.GameVersion != "1.4.8") throw new IOException("Dünya paketi veya sürümü bu programla uyumlu değil.");
        foreach (var pair in new[] { (Name: "world.sav", Hash: manifest.SavSha256), (Name: "world.json", Hash: manifest.JsonSha256) })
        {
            using var input = zip.GetEntry(pair.Name)!.Open();
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(pair.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Dünya paketindeki dosyalardan biri bozuk.");
        }
        using var jsonInput = zip.GetEntry("world.json")!.Open();
        using var json = JsonDocument.Parse(jsonInput);
        if (!json.RootElement.TryGetProperty("Players", out var players) || players.ValueKind != JsonValueKind.Array) throw new IOException("Oyuncu kaydı doğrulanamadı.");
        return manifest;
    }
    public static void Import(AppSettings settings, string package)
    {
        var controller = new HostController(settings);
        if (controller.ManagedRunning || controller.OtherServerRunning()) throw new IOException("Dünya alırken sunucu kapalı olmalı.");
        var manifest = Inspect(package);
        var state = ReadState(settings);
        if (state.PackageId == manifest.PackageId) throw new IOException("Bu dünya paketi zaten kullanıldı. Son hosttan güncel devir paketini al.");
        if (state.WorldId == manifest.WorldId && (manifest.Generation < state.Generation || state.HandedOff && manifest.Generation <= state.Generation))
            throw new IOException("Bu paket daha önce devrettiğin veya daha eski bir dünya. Son hostun yeni devrettiği paketi al.");
        MachineSetup.EnsureConfig(settings);
        var folder = Path.Combine(settings.DataDir, "Game Saves");
        Directory.CreateDirectory(folder);
        var old = SaveStamp.Read(settings, manifest.SaveName);
        string? backup = null;
        if (old.SavTicks > 0 || old.JsonTicks > 0) backup = controller.Backup(manifest.SaveName, "Dunya-almadan");
        var stage = Path.Combine(settings.DataDir, "Import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        using (var zip = ZipFile.OpenRead(package))
            foreach (var extension in new[] { ".sav", ".json" }) zip.GetEntry("world" + extension)!.ExtractToFile(Path.Combine(stage, manifest.SaveName + extension));
        var configPath = ServerConfig.PathFor(settings);
        var originalConfig = File.ReadAllText(configPath);
        try
        {
            foreach (var extension in new[] { ".sav", ".json" }) File.Move(Path.Combine(stage, manifest.SaveName + extension), Path.Combine(folder, manifest.SaveName + extension), true);
            controller.ValidatePair(manifest.SaveName);
            var config = JsonNode.Parse(originalConfig, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            config["saveName"] = manifest.SaveName;
            Files.Write(configPath, config);
            Files.Write(StatePath(settings), new WorldState(manifest.WorldId, manifest.Generation, false, manifest.PackageId));
        }
        catch
        {
            foreach (var extension in new[] { ".sav", ".json" })
            {
                var target = Path.Combine(folder, manifest.SaveName + extension);
                if (backup != null) File.Copy(Path.Combine(backup, manifest.SaveName + extension), target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
            File.WriteAllText(configPath, originalConfig);
            throw;
        }
    }
}
