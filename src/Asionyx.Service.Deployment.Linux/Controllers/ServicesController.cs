using Microsoft.AspNetCore.Mvc;
using Asionyx.Service.Deployment.Linux.Services;

namespace Asionyx.Service.Deployment.Linux.Controllers;

[ApiController]
[Route("api/services-minimal")]
public class DummyServicesController : ControllerBase
{
    private readonly IAuditStore _audit;

    public DummyServicesController(IAuditStore audit)
    {
        _audit = audit;
    }

    [HttpGet("list")]
    public IActionResult List() => Ok(new { services = new[] { "hello-deploy" } });
}
