using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace AudioRouter.Controllers;

[ApiController]
[Route("info")]
[AllowAnonymous]
public class InfoController : ControllerBase
{
    private readonly IConfiguration _config;

    public InfoController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet]
    public IActionResult Get()
    {
        // Determine provider from configuration (matches Program.cs provider selection)
        var provider = _config["provider"] ?? _config["Provider"] ?? "JackSharpCore";

        // Determine version from entry assembly
        string version = "unknown";
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            version = asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch { }

        return Ok(new { provider = provider.ToLowerInvariant() == "soundflow" ? "soundflow" : "jacksharp", version });
    }
}
