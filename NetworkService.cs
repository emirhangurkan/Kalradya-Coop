using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KalradyaCoop;

public static class NetworkService
{
    public static string TailscaleExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
    public static string TailscaleGui => Path.Combine(Path.GetDirectoryName(TailscaleExe)!, "tailscale-ipn.exe");
    private static string PowerShellExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string exe, IEnumerable<string> args, int timeoutSeconds = 12)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in args) p.StartInfo.ArgumentList.Add(arg);
        p.Start();
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new IOException("İşlem zamanında yanıt vermedi."); }
        return (p.ExitCode, await output, await error);
    }
    public static async Task<NetworkState> ReadAsync()
    {
        try
        {
            var r = await RunAsync(TailscaleExe, new[] { "status", "--json" }, 6);
            using var doc = JsonDocument.Parse(r.Output);
            var root = doc.RootElement;
            var state = root.GetProperty("BackendState").GetString() ?? "";
            var ip = "";
            if (root.TryGetProperty("Self", out var self) && self.ValueKind == JsonValueKind.Object && self.TryGetProperty("TailscaleIPs", out var ips)) ip = ips.EnumerateArray().Select(x => x.GetString() ?? "").FirstOrDefault(x => !x.Contains(':')) ?? "";
            List<NetPeer> peers = [];
            if (root.TryGetProperty("Peer", out var peer) && peer.ValueKind == JsonValueKind.Object)
                foreach (var property in peer.EnumerateObject())
                {
                    var v = property.Value;
                    var address = v.GetProperty("TailscaleIPs").EnumerateArray().Select(x => x.GetString() ?? "").FirstOrDefault(x => !x.Contains(':')) ?? "";
                    peers.Add(new(address, v.TryGetProperty("Online", out var online) && online.GetBoolean(), v.TryGetProperty("Active", out var active) && active.GetBoolean()));
                }
            return new(state == "Running" && ip.Length > 0, ip, peers, state);
        }
        catch { return new(false, "", [], File.Exists(TailscaleExe) ? "Bağlı değil" : "Tailscale bulunamadı"); }
    }
    public static async Task<NetworkState> ConnectAsync()
    {
        if (!File.Exists(TailscaleExe)) throw new IOException("Tailscale bulunamadı. Önce Tailscale'i kur.");
        var before = await ReadAsync();
        if (before.Connected) return before;
        try { await RunAsync(TailscaleExe, new[] { "up" }, 15); } catch { }
        for (var i = 0; i < 15; i++)
        {
            var state = await ReadAsync();
            if (state.Connected) return state;
            if (state.Detail == "NeedsLogin") break;
            await Task.Delay(1000);
        }
        if (File.Exists(TailscaleGui)) Process.Start(new ProcessStartInfo(TailscaleGui) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        throw new IOException("Tailscale uygulamasında hesabına giriş yap, ardından yeniden Başlat'a bas.");
    }
    public static async Task DisconnectAsync()
    {
        var r = await RunAsync(TailscaleExe, new[] { "down" });
        if (r.ExitCode != 0) throw new IOException("Sunucu kapandı; Tailscale bağlantısı kesilemedi.");
    }
    private static string Q(string value) => "'" + value.Replace("'", "''") + "'";
    public static async Task<bool> CheckFirewallAsync(AppSettings settings, string ip, int port)
    {
        var script = "$ErrorActionPreference='Stop'; $r=Get-NetFirewallRule -Name 'BannerlordCoop-Tailscale-UDP4200-Friend'; $a=$r|Get-NetFirewallAddressFilter; $p=$r|Get-NetFirewallPortFilter; $i=$r|Get-NetFirewallInterfaceFilter; $app=$r|Get-NetFirewallApplicationFilter; $profile=Get-NetFirewallProfile -PolicyStore ActiveStore -Name Private; $n=Get-NetConnectionProfile -InterfaceAlias 'Tailscale'; [pscustomobject]@{ok=($r.Enabled.ToString() -eq 'True' -and $r.Action.ToString() -eq 'Allow' -and $profile.Enabled.ToString() -eq 'True' -and $profile.AllowInboundRules.ToString() -eq 'True' -and $n.NetworkCategory.ToString() -eq 'Private' -and $a.LocalAddress -contains " + Q(ip) + " -and ($a.RemoteAddress -contains '100.64.0.0/255.192.0.0' -or $a.RemoteAddress -contains '100.64.0.0/10') -and $p.LocalPort -eq " + Q(port.ToString()) + " -and $p.Protocol -eq 'UDP' -and $i.InterfaceAlias -contains 'Tailscale' -and $app.Program -eq " + Q(Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "engine", "dotnet", "dotnet.exe")) + ")}|ConvertTo-Json -Compress";
        try
        {
            var r = await RunAsync(PowerShellExe, new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }, 35);
            return JsonDocument.Parse(r.Output).RootElement.GetProperty("ok").GetBoolean();
        }
        catch { return false; }
    }
    public static async Task RepairFirewallAsync(AppSettings settings, string ip, int port)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || port is < 1 or > 65535) throw new IOException("Bağlantı adresi doğrulanamadı.");
        var folder = Path.Combine(Files.Home, "NetworkRepair", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var result = Path.Combine(folder, "result.json");
        var program = Path.Combine(Path.GetDirectoryName(settings.ServerExe)!, "engine", "dotnet", "dotnet.exe");
        var script = "$ErrorActionPreference='Stop'\ntry {\n" +
            "$before=[pscustomobject]@{Private=Get-NetFirewallProfile -Name Private;Rules=@(Get-NetFirewallRule -DisplayName 'Tailscale-In' -ErrorAction SilentlyContinue|Select-Object Name,Enabled)}; $before|ConvertTo-Json -Depth 5|Set-Content -LiteralPath " + Q(Path.Combine(folder, "before.json")) + " -Encoding UTF8\n" +
            "$name='BannerlordCoop-Tailscale-UDP4200-Friend'\n$params=@{Direction='Inbound';Action='Allow';Enabled='True';Profile='Private';Protocol='UDP';LocalPort=" + port + ";LocalAddress=" + Q(ip) + ";RemoteAddress='100.64.0.0/10';InterfaceAlias='Tailscale';Program=" + Q(program) + "}\n" +
            "if(Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue){Set-NetFirewallRule -Name $name @params}else{New-NetFirewallRule -Name $name -DisplayName 'Bannerlord Coop - Tailscale UDP' @params|Out-Null}\n" +
            "Get-NetFirewallRule -DisplayName 'Tailscale-In' -ErrorAction SilentlyContinue|Disable-NetFirewallRule\nSet-NetConnectionProfile -InterfaceAlias 'Tailscale' -NetworkCategory Private\nSet-NetFirewallProfile -Profile Private -Enabled True -AllowInboundRules True\n@{ok=$true}|ConvertTo-Json|Set-Content -LiteralPath " + Q(result) + " -Encoding UTF8\n} catch {@{ok=$false;error=$_.Exception.Message}|ConvertTo-Json|Set-Content -LiteralPath " + Q(result) + " -Encoding UTF8;exit 1}";
        var scriptPath = Path.Combine(folder, "repair.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
        using var p = Process.Start(new ProcessStartInfo(PowerShellExe) { Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"", UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new IOException("Yönetici onayı alınamadı.");
        await p.WaitForExitAsync();
        using var doc = JsonDocument.Parse(File.ReadAllText(result));
        if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("Ağ izni düzenlenemedi: " + doc.RootElement.GetProperty("error").GetString());
        if (!await CheckFirewallAsync(settings, ip, port)) throw new IOException("Ağ izni doğrulanamadı. Günlükleri kontrol et.");
    }
}
