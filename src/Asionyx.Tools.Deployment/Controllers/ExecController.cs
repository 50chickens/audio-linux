using Microsoft.AspNetCore.Mvc;
using Asionyx.Tools.Deployment.Services;
using System.Diagnostics;

namespace Asionyx.Tools.Deployment.Controllers;

[ApiController]
[Route("api/exec")]
public class ExecController : ControllerBase
{
    private readonly IAuditStore _audit;

    public ExecController(IAuditStore audit)
    {
        _audit = audit;
    }

    public record ExecRequest(string Shell, string? Code, string? FilePath, int TimeoutSeconds = 60);

    [HttpPost]
    public async Task<IActionResult> Execute([FromBody] ExecRequest req)
    {
        if (req == null) return BadRequest();
        var shell = req.Shell?.ToLowerInvariant();
        if (shell != "bash" && shell != "powershell") return BadRequest("Shell must be 'bash' or 'powershell'");

        string exe, args;
        if (!string.IsNullOrWhiteSpace(req.FilePath))
        {
            if (!System.IO.File.Exists(req.FilePath)) return BadRequest("File not found");
            if (shell == "bash") { exe = "/bin/bash"; args = req.FilePath; }
            else { exe = "pwsh"; args = $"-File \"{req.FilePath}\""; }
        }
        else
        {
            // run code from string
            if (shell == "bash") { exe = "/bin/bash"; args = "-s"; }
            else { exe = "pwsh"; args = "-Command -"; }
        }

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardInput = string.IsNullOrWhiteSpace(req.FilePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var p = Process.Start(psi)!;
        if (p == null) return StatusCode(500, "Failed to start process");

        if (string.IsNullOrWhiteSpace(req.FilePath) && !string.IsNullOrEmpty(req.Code))
        {
            await p.StandardInput.WriteAsync(req.Code);
            p.StandardInput.Close();
        }

        var isExited = p.WaitForExit(req.TimeoutSeconds * 1000);
        if (!isExited)
        {
            try { p.Kill(); } catch { }
            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "Exec", "Timeout", null, DateTime.UtcNow));
            return StatusCode(504, "Process timed out");
        }

        var output = await p.StandardOutput.ReadToEndAsync();
        var error = await p.StandardError.ReadToEndAsync();

        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "Exec", "Success", output + "\n" + error, DateTime.UtcNow));

        return Ok(new { ExitCode = p.ExitCode, Output = output, Error = error });
    }
}
