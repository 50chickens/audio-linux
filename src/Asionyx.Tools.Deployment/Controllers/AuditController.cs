using Microsoft.AspNetCore.Mvc;
using Asionyx.Service.Deployment.Linux.Services;

namespace Asionyx.Service.Deployment.Linux.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditStore _audit;

    public AuditController(IAuditStore audit)
    {
        _audit = audit;
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var entries = await _audit.QueryAsync();
        return Ok(entries);
    }
}
