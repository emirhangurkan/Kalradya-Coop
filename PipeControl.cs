using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace KalradyaCoop;

// Same-user IPC to our independent worker. The unchanged production server
// receives only its supported stdin commands: status/save/stop.
public static class PipeControl
{
    public static bool ProcessMatches(int pid, DateTime started, string? exe = null)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && Math.Abs((p.StartTime.ToUniversalTime() - started).TotalMilliseconds) < 50 && (exe == null || string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
    public static string Name(Session session) => "KalradyaCoop.Control." + session.RunToken;
    public static async Task<WorkerStatus> SendAsync(Session session, string command)
    {
        if (command is not ("status" or "save" or "stop")) throw new IOException("Geçersiz sunucu komutu.");
        if (!ProcessMatches(session.WorkerPid, session.WorkerStartedUtc, session.WorkerExe)) throw new IOException("Sunucu denetimi açık değil.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        using var pipe = new NamedPipeClientStream(".", Name(session), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { token = session.RunToken, command }));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Sunucu yanıt vermedi.");
        if (line.Length > 16384) throw new IOException("Sunucu yanıtı geçersiz.");
        var status = JsonSerializer.Deserialize<WorkerStatus>(line, Files.Json) ?? throw new IOException("Sunucu yanıtı okunamadı.");
        if (status.RunToken != session.RunToken || status.WorkerPid != session.WorkerPid || status.WorkerStartedUtc != session.WorkerStartedUtc) throw new IOException("Sunucu kimliği uyuşmuyor.");
        if (status.Error.Length > 0) throw new IOException(status.Error);
        return status;
    }
}

public sealed record WorkerStatus(string RunToken, int WorkerPid, DateTime WorkerStartedUtc, bool Ready, int? Players, string Phase, string Error);
