using System.IO;
using System.Net;
namespace KalradyaCoop;
public sealed record JoinDetails(string Address, string Password, string Invite)
{
    public static JoinDetails Parse(string text)
    {
        if (text.Length > 16384) throw new IOException("Katılım mesajı çok uzun.");
        string address = "", password = "", invite = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Adres:", StringComparison.OrdinalIgnoreCase)) address = line[6..].Trim();
            else if (line.StartsWith("Şifre:", StringComparison.OrdinalIgnoreCase)) password = line[6..].Trim();
            else if (line.StartsWith("Tailscale daveti:", StringComparison.OrdinalIgnoreCase)) invite = line["Tailscale daveti:".Length..].Trim();
        }
        if (address.Length == 0) address = text.Trim();
        ValidateAddress(address);
        if (invite.Length > 0 && !Files.IsInvite(invite)) throw new IOException("Davet bağlantısı Tailscale'e ait değil.");
        return new(address, password == "Şifre yok" ? "" : password, invite);
    }
    public static void ValidateAddress(string address)
    {
        var parts = address.Trim().Split(':');
        if (parts.Length > 2 || parts[0].Split('.').Length != 4 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || parts.Length == 2 && (!int.TryParse(parts[1], out var port) || port < 1 || port > 65535))
            throw new IOException("Hostun IPv4 adresini yaz. Örnek biçim: 100.x.x.x:4200");
    }
    public static void CheckHostPresence(string address, NetworkState network)
    {
        ValidateAddress(address);
        var ip = IPAddress.Parse(address.Trim().Split(':')[0]);
        var bytes = ip.GetAddressBytes();
        if (bytes[0] != 100 || bytes[1] < 64 || bytes[1] > 127 || ip.ToString() == network.Ip) return;
        var peer = network.Peers.FirstOrDefault(x => x.Ip == ip.ToString());
        if (peer == null) throw new IOException("Host Tailscale ağında görünmüyor. Onun davetini, uygulamada giriş yaptığın hesapla kabul et.");
        if (!peer.Online) throw new IOException("Host Tailscale'de çevrimdışı. Arkadaşın Tailscale'i ve sunucuyu açınca tekrar dene.");
    }
}
