using Microsoft.AspNetCore.Mvc;
using AudioRouter.Library.Core;
using AudioRouter.Library.Audio.JackSharp;

namespace AudioRouter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController : ControllerBase
{
    private readonly IAudioRouter _router;

    public AudioController(IAudioRouter router)
    {
        _router = router;
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] RouteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FromDevice) || string.IsNullOrWhiteSpace(request.ToDevice))
            return BadRequest("FromDevice and ToDevice are required.");

        var ok = _router.StartRoute(request);
        return ok ? Ok(new { status = "started" }) : StatusCode(500, new { status = "failed" });
    }

    [HttpPost("stop")]
    public IActionResult Stop([FromBody] RouteRequest? request)
    {
        var ok = _router.StopRoute(request);
        return ok ? Ok(new { status = "stopped" }) : StatusCode(500, new { status = "failed" });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new { isRouting = _router.IsRouting });
    }
}
