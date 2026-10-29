using System.IO;
namespace KalradyaCoop;
public static class SetupVerification
{
    public static async Task<int> RunAsync(string report)
    {
        var root = Path.Combine(Files.Home, "Verification", "setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var checks = new List<string>();
        try
        {
            var real = Files.Settings();
            var test = new AppSettings { ServerExe = Path.Combine(root, "Server", "BannerlordCoopServer.exe"), GameDir = Path.Combine(root, "Game"), DataDir = Path.Combine(root, "Data") };
            Directory.CreateDirectory(Path.Combine(test.GameDir, "Modules", "Native"));
            File.Copy(Path.Combine(real.GameDir, "Modules", "Native", "SubModule.xml"), Path.Combine(test.GameDir, "Modules", "Native", "SubModule.xml"));
            void Progress(string message) => Files.Write(report, new { status = "running", message, root, checks });
            Progress("Verifying official installation packages");
            var zip = await MachineSetup.DownloadAsync(MachineSetup.ModUrl, "BannerlordCoop-v0.1.6.zip", MachineSetup.ModHash, Progress, CancellationToken.None);
            var jobFile = Path.Combine(root, "install-mod.json");
            Files.Write(jobFile, new ModInstallJob(test.GameDir, zip, Path.Combine(root, "mod-result.txt")));
            if (MachineSetup.InstallModElevated(jobFile) != 0 || !MachineSetup.ModInstalled(test)) throw new IOException("Isolated mod install failed.");
            checks.Add("Install the pinned official Coop ZIP into an empty game fixture and verify its module hash");
            await MachineSetup.InstallServerAsync(test, Progress, CancellationToken.None);
            if (MachineSetup.Hash(test.ServerExe) != MachineSetup.Hash(real.ServerExe)) throw new IOException("Extracted server binary mismatch.");
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(test.ServerExe)!, "engine", "dotnet", "dotnet.exe"))) throw new IOException("Extracted engine missing.");
            checks.Add("Verify the 4.4 GB official server archive hash and extract a complete new server installation");
            var config = ServerConfig.Read(test);
            if (config.Password.Length < 8 || config.Port != 4200 || config.Autosave != 5) throw new IOException("Fresh-machine server defaults failed.");
            if (MachineSetup.EulaAccepted(test)) throw new IOException("Fresh install incorrectly pre-accepted the license.");
            checks.Add("Create independent host defaults and leave first-use license acceptance to the new host");
            Files.Write(report, new { status = "passed", checks, root, completedUtc = DateTime.UtcNow });
            return 0;
        }
        catch (Exception ex) { Files.Write(report, new { status = "failed", checks, root, error = ex.ToString() }); return 1; }
    }
}
