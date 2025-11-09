using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace Asionyx.Tools.Deployment.IntegrationTests;

public class MiddlewareTests
{
    private WebApplicationFactory<Program>? _factory;

    [SetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("DEPLOY_API_KEY", "testkey");
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(temp);
        // Ensure a logs folder and a minimal deployment.log exists so the LogsController can return results
        var logsDir = System.IO.Path.Combine(temp, "logs");
        System.IO.Directory.CreateDirectory(logsDir);
        var logFile = System.IO.Path.Combine(logsDir, "deployment.log");
        // write a single JSON-line entry
        var entry = System.Text.Json.JsonSerializer.Serialize(new { datestamp = DateTime.UtcNow.ToString("o"), Level = "info", E = (object?)null, payload = new { Message = "test init" } });
        System.IO.File.WriteAllText(logFile, entry + "\n");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseContentRoot(temp));
    }

    [TearDown]
    public void TearDown()
    {
        try { _factory?.Dispose(); } catch { }
    }

    [Test]
    public async Task HealthEndpoint_AllowsAnonymous()
    {
        using var client = _factory!.CreateClient();
        // No API key header
        var r = await client.GetAsync("/health");
    Assert.That(r.IsSuccessStatusCode, Is.True, $"Health endpoint should be public, got {(int)r.StatusCode}");
    }

    [Test]
    public async Task LogsEndpoint_RequiresApiKey()
    {
        using var client = _factory!.CreateClient();

        // No API key should be unauthorized
        var r = await client.GetAsync("/api/logs");
    Assert.That(r.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Unauthorized));

        // With API key should succeed
        client.DefaultRequestHeaders.Add("X-Api-Key", "testkey");
        var r2 = await client.GetAsync("/api/logs");
    Assert.That(r2.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
    }
}
