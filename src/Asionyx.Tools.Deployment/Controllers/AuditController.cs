using Microsoft.AspNetCore.Mvc;
using Asionyx.Tools.Deployment.Services;

namespace Asionyx.Tools.Deployment.Controllers;

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
