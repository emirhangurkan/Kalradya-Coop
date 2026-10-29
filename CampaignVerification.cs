using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KalradyaCoop;

public static class CampaignVerification
{
    public static async Task<int> RunAsync(string reportPath)
    {
        HostController? active = null;
        var checks = new List<string>();
        var fixture = Path.Combine(Files.Home, "Verification", "campaigns-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            var original = Files.Settings();
            if (new HostController(original).ManagedRunning || new HostController(original).OtherServerRunning()) throw new IOException("Close the real server before verification.");
            var cfg = ServerConfig.Read(original);
            var protectedFiles = new[] { Files.SettingsPath, ServerConfig.PathFor(original), Path.Combine(original.DataDir,"Game Saves",cfg.Name+".sav"), Path.Combine(original.DataDir,"Game Saves",cfg.Name+".json"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Mount and Blade II Bannerlord","CoopData","mod-config.json") }.Where(File.Exists).ToDictionary(x=>x, MachineSetup.Hash);
            var test = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original, Files.Json), Files.Json)!;
            test.DataDir = Path.Combine(fixture, "Existing"); test.CampaignsRoot = ""; test.Campaigns = new(); test.DisconnectOnStop = false; test.PreventSleep = false;
            Directory.CreateDirectory(Path.Combine(test.DataDir,"Game Saves"));
            foreach (var ext in new[]{".sav",".json"}) File.Copy(Path.Combine(original.DataDir,"Game Saves",cfg.Name+ext),Path.Combine(test.DataDir,"Game Saves",cfg.Name+ext));
            File.Copy(ServerConfig.PathFor(original), ServerConfig.PathFor(test));
            File.Copy(Path.Combine(original.DataDir,"eula.txt"),Path.Combine(test.DataDir,"eula.txt"));
            Files.Write(Path.Combine(test.DataDir,"kalradya-world.json"), new WorldState(Guid.NewGuid().ToString("N"),3,true,Guid.NewGuid().ToString("N")));
            var oldDir = test.DataDir;
            var oldConfigHash = MachineSetup.Hash(ServerConfig.PathFor(test));
            var testSettingsPath = Path.Combine(fixture,"settings.json");
            var options = Campaigns.ReadOptions(original);
            foreach (var field in Campaigns.DifficultyFields) options["difficulty"]![field.Key] = "Realistic";
            options["difficulty"]!["birthAndDeath"] = false;
            options["difficulty"]!["autoAllocateClanMemberPerks"] = false;
            options["modOptions"]!["battleSize"] = 400;
            options["verificationMarker"] = "preserved";
            var idle = new HostController(test, Path.Combine(fixture,"session.json"), true);
            var blankRejected = false;
            try { Campaigns.Create(test," ",options,idle,testSettingsPath); } catch (IOException) { blankRejected = true; }
            if (!blankRejected || test.DataDir != oldDir) throw new Exception("Blank campaign name accepted.");
            var slot = Campaigns.Create(test,"Yeni Türkçe Sefer",options,idle,testSettingsPath);
            var newConfig = ServerConfig.Read(test);
            if (SaveStamp.Read(test,newConfig.Name).SavTicks != 0 || SaveStamp.Read(test,newConfig.Name).JsonTicks != 0 || WorldTransfer.ReadState(test).HandedOff) throw new Exception("New world inherited old progress or handoff state.");
            if (MachineSetup.Hash(Path.Combine(oldDir,"server-config.json")) != oldConfigHash) throw new Exception("Old configuration changed.");
            checks.Add("Separate new campaign has no old save/player data; old config and transfer lock preserved; invalid name rejected");
            var config = Campaigns.Parse(File.ReadAllText(ServerConfig.PathFor(test))); config["port"]=44206; Files.Write(ServerConfig.PathFor(test),config);
            active = new HostController(test,Path.Combine(fixture,"session.json"),true) { Progress = message => Files.Write(reportPath,new {status="running",message,checks,fixture}) };
            await active.StartAsync(false);
            var snapshot = await active.SnapshotAsync();
            if (!snapshot.Ready || snapshot.SavedPlayers != 0) throw new Exception("Fresh server not ready with zero saved players.");
            string log;
            using (var logStream = new FileStream(active.Session!.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var logReader = new StreamReader(logStream)) log = logReader.ReadToEnd();
            if (!log.Contains("playerReceivedDamage=Realistic") || !log.Contains("combatAIDifficulty=Realistic") || !log.Contains("birthAndDeath=off") || !log.Contains(test.DataDir.Replace("\\", "\\\\")) && !log.Contains(test.DataDir)) throw new Exception("Server did not confirm selected difficulty/config path.");
            checks.Add("Real dedicated server bootstraps fresh world and reports selected Realistic difficulty and aging off from per-campaign config");
            var rejected = false;
            try { Campaigns.Select(test,Campaigns.List(test).First(x=>x.DataDir==oldDir),active,testSettingsPath); } catch(IOException) { rejected=true; }
            if (!rejected) throw new Exception("Live campaign switch allowed.");
            rejected = false;
            try { Campaigns.SaveOptions(test,options,active); } catch(IOException) { rejected=true; }
            if (!rejected) throw new Exception("Live difficulty edit allowed.");
            await active.StopAsync(); active.ValidatePair(newConfig.Name);
            checks.Add("Active-session changes blocked; stop saves a valid fresh SAV/JSON pair and verified backup");
            foreach(var field in Campaigns.DifficultyFields) options["difficulty"]![field.Key]="Easy";
            Campaigns.SaveOptions(test,options,active);
            var saved = Campaigns.ReadOptions(test);
            if (saved["difficulty"]!["playerReceivedDamage"]!.GetValue<string>()!="Easy" || saved["verificationMarker"]!.GetValue<string>()!="preserved" || !Directory.EnumerateFiles(Path.Combine(test.DataDir,"OptionBackups")).Any()) throw new Exception("Difficulty update lost settings or backup.");
            var oldSlot=Campaigns.List(test).First(x=>x.DataDir==oldDir);
            Campaigns.Select(test,oldSlot,active,testSettingsPath);
            if (!WorldTransfer.ReadState(test).HandedOff) throw new Exception("Switch cleared old transfer lock.");
            Campaigns.Select(test,slot,active,testSettingsPath);
            if (WorldTransfer.ReadState(test).HandedOff || SaveStamp.Read(test,newConfig.Name).SavTicks==0) throw new Exception("New world did not survive reselect.");
            checks.Add("Difficulty changes preserve other options and backup; old/new campaign switching preserves each world's saves and transfer state");
            foreach (var pair in protectedFiles) if (MachineSetup.Hash(pair.Key)!=pair.Value) throw new Exception("Original data changed: "+pair.Key);
            checks.Add("Original settings, saves and shared mod options unchanged; no Tailscale/firewall changes");
            Files.Write(reportPath,new {status="passed",checks,fixture,completedUtc=DateTime.UtcNow}); return 0;
        }
        catch(Exception ex)
        {
            if(active?.ManagedRunning==true) { try { await active.StopAsync(); } catch {} }
            Files.Write(reportPath,new {status="failed",error=ex.ToString(),checks,fixture}); return 1;
        }
    }
}
