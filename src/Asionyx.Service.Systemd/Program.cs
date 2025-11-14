using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
// Listen on port 5200 so the systemctl shim can reach this emulator at localhost:5200
builder.WebHost.UseUrls("http://0.0.0.0:5200");
builder.Services.AddSingleton<ServiceManager>();
builder.Services.AddControllers();
var app = builder.Build();

app.MapPost("/daemon-reload", (ServiceManager m) => Results.Ok(new { ok = true }));

app.MapGet("/unit/{name}/status", ([FromRoute] string name, ServiceManager m) =>
{
    var s = m.GetStatus(name);
    if (s == null) return Results.NotFound();
    return Results.Ok(s);
});

app.MapPost("/unit/{name}/start", async ([FromRoute] string name, ServiceManager m) =>
{
    var ok = await m.Start(name);
    return ok ? Results.Ok(new { Exit = 0 }) : Results.StatusCode(500);
});

app.MapPost("/unit/{name}/stop", async ([FromRoute] string name, ServiceManager m) =>
{
    var ok = await m.Stop(name);
    return ok ? Results.Ok(new { Exit = 0 }) : Results.StatusCode(500);
});

app.MapPost("/unit/{name}/restart", async ([FromRoute] string name, ServiceManager m) =>
{
    await m.Stop(name);
    var ok = await m.Start(name);
    return ok ? Results.Ok(new { Exit = 0 }) : Results.StatusCode(500);
});

app.MapGet("/units", (ServiceManager m) => Results.Ok(m.ListUnits()));

app.MapGet("/logs/{name}", ([FromRoute] string name, ServiceManager m) =>
{
    var logs = m.GetLogs(name);
    if (logs == null) return Results.NotFound();
    return Results.Text(logs);
});

app.MapControllers();

app.Run();

internal class UnitStatus
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Running { get; set; }
    public int? Pid { get; set; }
}

internal class ServiceManager
{
    private readonly string _unitDir = "/etc/systemd/system";
    private readonly Dictionary<string, Process?> _running = new();
    private readonly object _lock = new();

    public ServiceManager()
    {
        try
        {
            if (!Directory.Exists(_unitDir)) Directory.CreateDirectory(_unitDir);
        }
        catch { }
    }

    public IEnumerable<string> ListUnits()
    {
        if (!Directory.Exists(_unitDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_unitDir, "*.service").Select(p => Path.GetFileNameWithoutExtension(p));
    }

    public UnitStatus? GetStatus(string name)
    {
        var enabled = File.Exists(Path.Combine(_unitDir, name + ".service"));
        lock (_lock)
        {
            var running = _running.TryGetValue(name, out var p) && p != null && !p.HasExited;
            return new UnitStatus { Name = name, Enabled = enabled, Running = running, Pid = running ? _running[name]?.Id : null };
        }
    }

    private string? ReadExecStart(string name)
    {
        var path = Path.Combine(_unitDir, name + ".service");
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        // Very simple parse for ExecStart
        var m = Regex.Match(text, @"^ExecStart=(.+)$", RegexOptions.Multiline);
        if (!m.Success) return null;
        return m.Groups[1].Value.Trim();
    }

    public async Task<bool> Start(string name)
    {
        var cmd = ReadExecStart(name);
        if (cmd == null) return false;
        try
        {
            // Use /bin/sh -c "<cmd>"
            var psi = new ProcessStartInfo("/bin/sh", "-c \"" + cmd.Replace("\"", "\\\"") + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = new Process() { StartInfo = psi };
            p.Start();
            lock (_lock)
            {
                _running[name] = p;
            }
            // fire-and-forget capture
            _ = Task.Run(async () =>
            {
                try
                {
                    var outt = await p.StandardOutput.ReadToEndAsync();
                    var err = await p.StandardError.ReadToEndAsync();
                    var logPath = Path.Combine("/var/log/asionyx-systemd", name + ".log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? "/var/log/asionyx-systemd");
                    await File.AppendAllTextAsync(logPath, $"[{DateTime.UtcNow:o}] stdout:\n{outt}\n stderr:\n{err}\n");
                }
                catch { }
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> Stop(string name)
    {
        lock (_lock)
        {
            if (!_running.TryGetValue(name, out var p) || p == null || p.HasExited) return Task.FromResult(true);
            try
            {
                p.Kill(true);
            }
            catch { }
            _running[name] = null;
            return Task.FromResult(true);
        }
    }

    public string? GetLogs(string name)
    {
        var logPath = Path.Combine("/var/log/asionyx-systemd", name + ".log");
        if (!File.Exists(logPath)) return null;
        return File.ReadAllText(logPath);
    }
}
