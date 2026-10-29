using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KalradyaCoop;

public static class HostWorker
{
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    public static async Task<int> RunAsync(string launchPath)
    {
        var launch = Files.Read<WorkerLaunch>(launchPath) ?? throw new IOException("Başlatma dosyası okunamadı.");
        var settings = launch.Settings;
        var mutexName = "Local\\KalradyaCoop.Server." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(settings.DataDir).ToLowerInvariant())))[..20];
        using var mutex = new Mutex(true, mutexName, out var owner);
        if (!owner) return 2;
        var session = new Session { DataDir = settings.DataDir, ServerExe = settings.ServerExe, RunToken = "kalradya-" + Guid.NewGuid().ToString("N"), WorkerExe = Environment.ProcessPath!, WorkerPid = Environment.ProcessId, WorkerStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(), LogPath = launch.LogPath };
        using var sleepStop = new ManualResetEvent(false);
        using var stopped = new CancellationTokenSource();
        Thread? sleepThread = null;
        var stateLock = new object();
        bool ready = false;
        int? players = null;
        string phase = "loading";
        var stdinLock = new SemaphoreSlim(1, 1);
        Process? server = null;
        async Task SendLine(string line)
        {
            await stdinLock.WaitAsync();
            try { if (server is not { HasExited: false }) throw new IOException("Sunucu kapalı."); await server.StandardInput.WriteLineAsync(line); await server.StandardInput.FlushAsync(); }
            finally { stdinLock.Release(); }
        }
        async Task ServeControl()
        {
            while (!stopped.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeControl.Name(session), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stopped.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                    var line = await reader.ReadLineAsync(requestTimeout.Token);
                    if (line == null || line.Length > 4096) continue;
                    using var request = JsonDocument.Parse(line);
                    if (request.RootElement.GetProperty("token").GetString() != session.RunToken) continue;
                    var command = request.RootElement.GetProperty("command").GetString();
                    string error = "";
                    try
                    {
                        if (command is not ("status" or "save" or "stop")) throw new IOException("Geçersiz komut.");
                        if (command == "save" && !ready) throw new IOException("Dünya henüz hazır değil.");
                        await SendLine(command!);
                    }
                    catch (Exception ex) { error = ex.Message; }
                    WorkerStatus status;
                    lock (stateLock) status = new(session.RunToken, session.WorkerPid, session.WorkerStartedUtc, ready, players, phase, error);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(status));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!stopped.IsCancellationRequested) Files.Log("Worker control: " + ex.Message); }
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(launch.LogPath)!);
            using var log = new StreamWriter(new FileStream(launch.LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            server = new Process { StartInfo = new ProcessStartInfo(settings.ServerExe) { WorkingDirectory = Path.GetDirectoryName(settings.ServerExe), UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
            server.StartInfo.ArgumentList.Add("--no-tui");
            server.StartInfo.ArgumentList.Add("--data-dir");
            server.StartInfo.ArgumentList.Add(settings.DataDir);
            // A campaign's own options must win over an inherited shared config path.
            if (File.Exists(Path.Combine(settings.DataDir, "mod-config.json")))
                server.StartInfo.Environment["COOP_DATA_DIR"] = settings.DataDir;
            Files.Write(launch.SessionPath, session);
            server.Start();
            session.ServerPid = server.Id;
            session.ServerStartedUtc = server.StartTime.ToUniversalTime();
            session.Phase = "running";
            Files.Write(launch.SessionPath, session);
            object logLock = new();
            void LogLine(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (logLock) log.WriteLine(e.Data);
                lock (stateLock)
                {
                    if (e.Data.StartsWith("@DS@"))
                    {
                        try
                        {
                            using var data = JsonDocument.Parse(e.Data[4..]);
                            var root = data.RootElement;
                            if (root.GetProperty("ev").GetString() == "state") { phase = root.GetProperty("phase").GetString() ?? ""; ready = phase == "serving"; }
                        }
                        catch { }
                    }
                    if (e.Data.StartsWith("[DedicatedServer] pulse:") || e.Data.StartsWith("[DedicatedServer] status:"))
                    {
                        var match = Regex.Match(e.Data, @"\bplayers=(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var count)) players = count;
                    }
                }
            }
            server.OutputDataReceived += LogLine;
            server.ErrorDataReceived += LogLine;
            server.BeginOutputReadLine();
            server.BeginErrorReadLine();
            var controls = ServeControl();
            if (settings.PreventSleep)
            {
                sleepThread = new Thread(() => { SetThreadExecutionState(0x80000001); sleepStop.WaitOne(); SetThreadExecutionState(0x80000000); }) { IsBackground = true };
                sleepThread.Start();
            }
            await server.WaitForExitAsync();
            server.WaitForExit();
            stopped.Cancel();
            await controls;
            session.Phase = "stopped";
            session.ExitCode = server.ExitCode;
            if (server.ExitCode != 0) session.Error = "Sunucu kapandı (kod " + server.ExitCode + "). Ayrıntılar günlükte.";
            Files.Write(launch.SessionPath, session);
            return server.ExitCode;
        }
        catch (Exception e)
        {
            session.Error = e.Message;
            session.Phase = "error";
            Files.Write(launch.SessionPath, session);
            Files.Log("Worker error: " + e.Message);
            return 1;
        }
        finally { stopped.Cancel(); sleepStop.Set(); sleepThread?.Join(2000); server?.Dispose(); }
    }
}

public sealed class WorkerLaunch
{
    public AppSettings Settings { get; set; } = new();
    public string SessionPath { get; set; } = "";
    public string LogPath { get; set; } = "";
}
