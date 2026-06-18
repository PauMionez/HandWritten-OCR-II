using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace HandWritten_OCR.Services;

public sealed class KrakenServerManager : IDisposable
{
    private static readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private const string HealthUrl = "http://localhost:5001/health";
    private const string PythonPath = @"D:\AI\venv\Scripts\python.exe";
    private static readonly string ScriptPath =
        Path.Combine(AppContext.BaseDirectory, "KrakenServer", "kraken_server_v2.py");

    private Process? _process;
    private bool _isReady;

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (_isReady == value) return;
            _isReady = value;
            IsReadyChanged?.Invoke(value);
        }
    }

    public event Action<bool>? IsReadyChanged;

    public async Task StartAsync()
    {
        // Pick up a server left running from a previous session — instant green dot on restart
        if (await CheckHealthAsync()) { IsReady = true; return; }

        if (_process is not null && !_process.HasExited) return;

        var psi = new ProcessStartInfo
        {
            FileName = PythonPath,
            Arguments = $"\"{ScriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            // Do NOT redirect stdout/stderr — unread pipes fill and block the Python process
        };
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";

        _process = Process.Start(psi);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200).ConfigureAwait(false);
            if (await CheckHealthAsync()) { IsReady = true; return; }
        }
    }

    private static async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var resp = await _healthClient.GetAsync(HealthUrl, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Stop()
    {
        if (_process is null || _process.HasExited) return;
        try { _process.Kill(entireProcessTree: true); }
        catch { }
        _process = null;
        IsReady = false;
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
    }
}
