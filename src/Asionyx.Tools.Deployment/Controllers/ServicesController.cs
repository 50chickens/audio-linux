using Microsoft.AspNetCore.Mvc;
using Asionyx.Service.Deployment.Linux.Services;
using System.Diagnostics;

namespace Asionyx.Service.Deployment.Linux.Controllers;

[ApiController]
[Route("api/services")]
public class ServicesController : ControllerBase
{
    private readonly IAuditStore _audit;

    public ServicesController(IAuditStore audit)
    {
        _audit = audit;
    }

    public record ServiceAction(string Name, string? UnitFileContent, string? Command);

    [HttpPost("install")]
    public async Task<IActionResult> Install([FromBody] ServiceAction req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required");

        if (OperatingSystem.IsWindows())
        {
            // use sc.exe to create service if UnitFileContent provided as path to exe args
            // Expect UnitFileContent to be the path to the exe or command line
            if (string.IsNullOrWhiteSpace(req.UnitFileContent)) return BadRequest("On Windows supply UnitFileContent = service binary path and args");
            var psi = new ProcessStartInfo("sc.exe", $"create {req.Name} binPath= \"{req.UnitFileContent}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi)!; p.WaitForExit();
            var outp = await p.StandardOutput.ReadToEndAsync();
            var err = await p.StandardError.ReadToEndAsync();
            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "InstallService", p.ExitCode==0?"Success":"Failed", outp+"\n"+err, DateTime.UtcNow));
            return Ok(new { Exit = p.ExitCode, Out = outp, Err = err });
        }

        // Linux: write unit file and enable
        if (!string.IsNullOrWhiteSpace(req.UnitFileContent))
        {
            var tmp = Path.Combine(Path.GetTempPath(), req.Name + ".service");
            System.IO.File.WriteAllText(tmp, req.UnitFileContent);
            // Run the multi-part command under a shell so shell operators (&&) are handled correctly.
            var cmd = $"sudo mv \"{tmp}\" /etc/systemd/system/{req.Name}.service && systemctl daemon-reload && systemctl enable --now {req.Name}";
            var psi = new ProcessStartInfo("/bin/sh", "-c \"" + cmd.Replace("\"", "\\\"") + "\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi)!; p.WaitForExit();
            var outp = await p.StandardOutput.ReadToEndAsync();
            var err = await p.StandardError.ReadToEndAsync();
            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "InstallService", p.ExitCode==0?"Success":"Failed", outp+"\n"+err, DateTime.UtcNow));
            return Ok(new { Exit = p.ExitCode, Out = outp, Err = err });
        }

        return BadRequest("UnitFileContent required on Linux");
    }

    [HttpPost("action")]
    public async Task<IActionResult> Action([FromBody] ServiceAction req)
    {
        var cmd = (req.Command ?? "start").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required");
        if (OperatingSystem.IsWindows())
        {
            string scArgs = cmd switch {
                "start" => $"start {req.Name}",
                "stop" => $"stop {req.Name}",
                "restart" => $"stop {req.Name} & sc.exe start {req.Name}",
                _ => $"start {req.Name}"
            };
            var psi = new ProcessStartInfo("sc.exe", scArgs) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi)!; p.WaitForExit();
            var outp = await p.StandardOutput.ReadToEndAsync();
            var err = await p.StandardError.ReadToEndAsync();
            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "ServiceAction", p.ExitCode==0?"Success":"Failed", outp+"\n"+err, DateTime.UtcNow));
            return Ok(new { Exit = p.ExitCode, Out = outp, Err = err });
        }
        string linuxCmd = cmd switch {
            "start" => $"systemctl start {req.Name}",
            "stop" => $"systemctl stop {req.Name}",
            "restart" => $"systemctl restart {req.Name}",
            _ => $"systemctl start {req.Name}"
        };
        var psiLinux = new ProcessStartInfo("sudo", linuxCmd) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var p2 = Process.Start(psiLinux)!; p2.WaitForExit();
        var out2 = await p2.StandardOutput.ReadToEndAsync();
        var err2 = await p2.StandardError.ReadToEndAsync();
        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "ServiceAction", p2.ExitCode==0?"Success":"Failed", out2+"\n"+err2, DateTime.UtcNow));
        return Ok(new { Exit = p2.ExitCode, Out = out2, Err = err2 });
    }
}
