using System.Net;
using Microsoft.AspNetCore.Http;

namespace Asionyx.Tools.Deployment.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly Asionyx.Tools.Deployment.Models.DeploymentOptions _options;

    public ApiKeyAuthMiddleware(RequestDelegate next, Asionyx.Tools.Deployment.Models.DeploymentOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow swagger, health checks, and public info endpoints without key
        // - swagger UI
        // - /health
        // - /info (allow anonymous access to any service info endpoint)
        if (context.Request.Path.StartsWithSegments("/swagger") || context.Request.Path == "/health" || context.Request.Path.StartsWithSegments("/info"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Missing API key");
            return;
        }

        var cfgKey = _options?.ApiKey ?? string.Empty;
        if (!string.Equals(providedKey, cfgKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsync("Invalid API key");
            return;
        }

        await _next(context);
    }
}
