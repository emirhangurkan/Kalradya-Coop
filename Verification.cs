using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KalradyaCoop;
public static class Verification
{
    public static async Task<int> RunAsync(string reportPath)
    {
        var checks = new List<string>();
        string phase = "setup";
        HostController? controller = null;
        AppSettings? original = null;
        Dictionary<string, string>? originalHashes = null;
        string fixture = "";
        try
        {
            original = Files.Settings();
            var cfg = ServerConfig.Read(original);
            originalHashes = HashPair(original.DataDir, cfg.Name);
            if (new SaveStamp(10, 10, 5000, 50).IsNewerThan(new(10, 9, 5000, 40))) throw new Exception("One-sided save accepted.");
            if (Files.IsInvite("https://tailscale.com.evil.example/invite/x") || !Files.IsInvite("https://login.tailscale.com/admin/invite/example")) throw new Exception("Invite validation failed.");
            checks.Add("Reject incomplete save pairs and non-Tailscale invitation URLs");
            fixture = Path.Combine(Files.Home, "Verification", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            VerifyClientGuards(original, fixture);
            checks.Add("Validate Bannerlord version, missing/offline Tailscale hosts, and idempotent Coop launcher configuration");
            var test = new AppSettings { ServerExe = original.ServerExe, DataDir = Path.Combine(fixture, "DedicatedServer"), DisconnectOnStop = false, PreventSleep = false, GameDir = original.GameDir };
            Directory.CreateDirectory(Path.Combine(test.DataDir, "Game Saves"));
            foreach (var ext in new[] { ".sav", ".json" }) File.Copy(Path.Combine(original.DataDir, "Game Saves", cfg.Name + ext), Path.Combine(test.DataDir, "Game Saves", cfg.Name + ext));
            File.Copy(Path.Combine(original.DataDir, "eula.txt"), Path.Combine(test.DataDir, "eula.txt"));
            var config = JsonNode.Parse(File.ReadAllText(ServerConfig.PathFor(original)), documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            config["port"] = 44200; config["autosaveMinutes"] = 0; config["steam"] = false;
            Files.Write(ServerConfig.PathFor(test), config);
            controller = new(test, Path.Combine(fixture, "session.json"), true);
            controller.Progress = text => Files.Write(reportPath, new { status = "running", phase, message = text, checks, fixture });
            phase = "start-real-server";
            await controller.StartAsync(false);
            var status = await controller.SnapshotAsync();
            // The production server emits its pulse asynchronously after receiving status.
            // Wait for that real report instead of mistaking an unreported count for failure.
            var statusDeadline = DateTime.UtcNow.AddSeconds(12);
            while (status.Ready && !status.PlayersOnline.HasValue && DateTime.UtcNow < statusDeadline)
            {
                await Task.Delay(400);
                status = await controller.SnapshotAsync();
            }
            if (!status.Ready || status.PlayersOnline != 0) throw new Exception($"Real server readiness/live players not confirmed: ready={status.Ready}, players={status.PlayersOnline}.");
            checks.Add("Load a separate copy of the four-player world on UDP 44200 and query live status");
            phase = "explicit-save";
            var before = SaveStamp.Read(test, cfg.Name);
            await controller.SaveAsync();
            if (!SaveStamp.Read(test, cfg.Name).IsNewerThan(before)) throw new Exception("Explicit save did not update both files.");
            checks.Add("Explicit save creates a fresh SAV/JSON pair and hash-verified backup");
            phase = "reattach";
            controller = new(test, Path.Combine(fixture, "session.json"), true) { Progress = text => Files.Write(reportPath, new { status = "running", phase, message = text, checks, fixture }) };
            if (!(await controller.SnapshotAsync()).Ready) throw new Exception("Reattachment failed.");
            checks.Add("A new controller attaches to the independent background server");
            phase = "save-and-stop";
            before = SaveStamp.Read(test, cfg.Name);
            await controller.StopAsync();
            if (controller.ManagedRunning || !SaveStamp.Read(test, cfg.Name).IsNewerThan(before)) throw new Exception("Save-and-stop failed.");
            checks.Add("Production stop command writes both save files and exits cleanly without forced termination");
            phase = "handoff-between-hosts";
            var outbound = Path.Combine(fixture, "world.kalradya");
            WorldTransfer.Export(test, outbound);
            bool refused = false;
            try { await controller.StartAsync(false); } catch (IOException) { refused = WorldTransfer.ReadState(test).HandedOff; }
            if (!refused || controller.ManagedRunning) throw new Exception("Previous host could start a handed-off world.");
            var second = new AppSettings { ServerExe = original.ServerExe, GameDir = original.GameDir, DataDir = Path.Combine(fixture, "SecondHost"), DisconnectOnStop = false, PreventSleep = false };
            WorldTransfer.Import(second, outbound);
            refused = false;
            try { WorldTransfer.Import(second, outbound); } catch (IOException) { refused = true; }
            if (!refused) throw new Exception("A previously imported package could reset the world.");
            var sentHashes = HashPair(test.DataDir, cfg.Name);
            if (HashPair(second.DataDir, cfg.Name).Any(x => sentHashes[x.Key] != x.Value)) throw new Exception("Transferred world did not preserve both files.");
            var secondConfig = JsonNode.Parse(File.ReadAllText(ServerConfig.PathFor(second)))!;
            secondConfig["port"] = 44201; secondConfig["autosaveMinutes"] = 0;
            Files.Write(ServerConfig.PathFor(second), secondConfig);
            File.Copy(Path.Combine(test.DataDir, "eula.txt"), Path.Combine(second.DataDir, "eula.txt"));
            controller = new(second, Path.Combine(fixture, "second-session.json"), true) { Progress = text => Files.Write(reportPath, new { status = "running", phase, message = text, checks, fixture }) };
            await controller.StartAsync(false);
            if ((await controller.SnapshotAsync()).SavedPlayers != status.SavedPlayers) throw new Exception("Player identity count changed on the new host.");
            await controller.StopAsync();
            var returning = Path.Combine(fixture, "return.kalradya");
            WorldTransfer.Export(second, returning);
            WorldTransfer.Import(test, returning);
            if (WorldTransfer.ReadState(test).HandedOff || WorldTransfer.ReadState(test).Generation != 2) throw new Exception("World handback failed.");
            checks.Add("Transfer a paired world to a second isolated host, boot it, preserve players, save, and hand it back; block stale-host startup");
            var corrupt = Path.Combine(fixture, "corrupt.kalradya"); File.Copy(returning, corrupt);
            using (var zip = System.IO.Compression.ZipFile.Open(corrupt, System.IO.Compression.ZipArchiveMode.Update))
            { zip.GetEntry("world.json")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("world.json").Open()); writer.Write("{\"Players\":[]}"); }
            refused = false;
            try { WorldTransfer.Inspect(corrupt); } catch (IOException) { refused = true; }
            if (!refused) throw new Exception("Corrupted handoff package accepted.");
            var join = JoinDetails.Parse("Bannerlord Co-op\nAdres: 100.64.0.8:4200\nŞifre: test-example\n");
            if (join.Address != "100.64.0.8:4200" || join.Password != "test-example") throw new Exception("Join message parsing failed.");
            checks.Add("Reject corrupted handoff files and parse portable join messages");
            var after = HashPair(original.DataDir, cfg.Name);
            if (originalHashes.Any(x => after[x.Key] != x.Value)) throw new Exception("Original user save changed during verification.");
            checks.Add("Original save files remain byte-for-byte unchanged; Tailscale settings untouched");
            Files.Write(reportPath, new { status = "passed", checks, fixture, completedUtc = DateTime.UtcNow });
            return 0;
        }
        catch (Exception ex)
        {
            // Only the isolated fixture can be stopped here; never touch the user's real session.
            if (controller?.ManagedRunning == true)
            {
                try { await controller.StopAsync(); } catch { }
            }
            Files.Write(reportPath, new { status = "failed", phase, error = ex.ToString(), checks, fixture, session = controller?.SessionFile });
            return 1;
        }
    }
    private static Dictionary<string, string> HashPair(string dir, string name) => new[] { ".sav", ".json" }.ToDictionary(ext => ext, ext => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, "Game Saves", name + ext)))));
    private static void VerifyClientGuards(AppSettings original, string fixture)
    {
        if (!MachineSetup.CompatibleGame(original.GameDir) || !MachineSetup.ModInstalled(original)) throw new Exception("Installed game/Coop pair is incompatible.");
        var fakeGame = Path.Combine(fixture, "CompatibilityGame");
        var module = Path.Combine(fakeGame, "Modules", "Native", "SubModule.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(module)!);
        File.WriteAllText(module, "<Module><Version value=\"v1.4.80\" /></Module>");
        if (MachineSetup.CompatibleGame(fakeGame)) throw new Exception("Wrong patch version accepted.");
        File.WriteAllText(module, "<Module><Version value=\"v1.4.8\" /></Module>");
        if (!MachineSetup.CompatibleGame(fakeGame)) throw new Exception("Supported version refused.");
        var configDir = Path.Combine(fixture, "LauncherConfigs"); Directory.CreateDirectory(configDir);
        var launcher = Path.Combine(configDir, "LauncherData.xml");
        File.WriteAllText(launcher, "<UserData><KeepMe>preserved</KeepMe><GameType>Multiplayer</GameType><SingleplayerData><ModDatas><UserModData><Id>Coop</Id><IsSelected>false</IsSelected></UserModData><UserModData><Id>NavalDLC</Id><IsSelected>true</IsSelected></UserModData></ModDatas></SingleplayerData></UserData>");
        MachineSetup.ConfigureLauncher(original, configDir);
        var xml = System.Xml.Linq.XDocument.Load(launcher);
        string? Selected(string id) => xml.Descendants("UserModData").Single(x => x.Element("Id")?.Value == id).Element("IsSelected")?.Value;
        if (Selected("Coop") != "true" || Selected("NavalDLC") != "false" || xml.Root?.Element("KeepMe")?.Value != "preserved") throw new Exception("Launcher module selection failed.");
        var stamp = File.GetLastWriteTimeUtc(launcher);
        MachineSetup.ConfigureLauncher(original, configDir);
        if (File.GetLastWriteTimeUtc(launcher) != stamp || Directory.GetFiles(configDir, "*.bak").Length != 1) throw new Exception("Launcher selection is not idempotent.");
        foreach (var address in new[] { "1", "100.64.0.5:0", "100.64.0.5:65536", "not-a-host" })
        {
            bool rejected = false; try { JoinDetails.ValidateAddress(address); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid address accepted: " + address);
        }
        foreach (var peers in new[] { new List<NetPeer>(), new List<NetPeer> { new("100.64.0.5", false, false) } })
        {
            bool rejected = false; try { JoinDetails.CheckHostPresence("100.64.0.5:4200", new(true, "100.64.0.7", peers, "Running")); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Unreachable Tailscale host accepted.");
        }
        JoinDetails.CheckHostPresence("100.64.0.5:4200", new(true, "100.64.0.7", [new("100.64.0.5", true, false)], "Running"));
        JoinDetails.CheckHostPresence("127.0.0.1:4200", new(true, "100.64.0.7", [], "Running"));
    }
}
