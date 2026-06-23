using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace HandWritten_OCR.Services;

public sealed class PaddleServerManager : IDisposable
{
    private static readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private const string HealthUrl = "http://localhost:5002/health";
    private static readonly string PythonPath =
        Path.Combine(AppContext.BaseDirectory, "PaddleVenv", "Scripts", "python.exe");
    private static readonly string ScriptPath =
        Path.Combine(AppContext.BaseDirectory, "PaddleServer", "paddle_server.py");

    private Process? _process;
    private bool _isReady;
    private bool _isStarting;

    // True only when PaddleVenv has been set up (setup_paddle_venv.bat was run)
    public bool IsVenvInstalled  => File.Exists(PythonPath);
    // True only when the server script was copied to the output folder (requires a build)
    public bool IsScriptDeployed => File.Exists(ScriptPath);

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

    public bool IsStarting
    {
        get => _isStarting;
        private set
        {
            if (_isStarting == value) return;
            _isStarting = value;
            IsStartingChanged?.Invoke(value);
        }
    }

    public event Action<bool>? IsReadyChanged;
    public event Action<bool>? IsStartingChanged;

    public async Task StartAsync()
    {
        if (IsReady) return;
        if (IsStarting) return;
        if (await CheckHealthAsync()) { IsReady = true; return; }

        if (_process is not null && !_process.HasExited) return;

        // Venv not set up yet — user must run setup_paddle_venv.bat first
        if (!File.Exists(PythonPath) || !File.Exists(ScriptPath)) return;

        IsStarting = true;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = PythonPath,
                Arguments       = $"\"{ScriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            _process = Process.Start(psi);

            // PaddleOCR first import is slow on low-spec — allow 120 s
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (await CheckHealthAsync()) { IsReady = true; return; }
            }
        }
        finally
        {
            IsStarting = false;
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
