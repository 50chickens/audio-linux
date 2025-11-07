using System;
using System.Threading.Tasks;
using System.Net.Http.Json;
using NUnit.Framework;
using Microsoft.AspNetCore.Mvc.Testing;
using AudioRouter;

namespace AudioRouter.IntegrationTests;

public class ApiIntegrationTests
{
    [Test]
    public async Task StartStopStatus_Workflow()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var startResp = await client.PostAsJsonAsync("/api/audio/start", new { FromDevice = "in", ToDevice = "out" });
        if (!startResp.IsSuccessStatusCode)
        {
            var txt = await startResp.Content.ReadAsStringAsync();
            Assert.Fail($"Start request failed: {(int)startResp.StatusCode} {startResp.StatusCode} - {txt}");
        }

        var statusResp = await client.GetAsync("/api/audio/status");
        if (!statusResp.IsSuccessStatusCode)
        {
            var txt = await statusResp.Content.ReadAsStringAsync();
            Assert.Fail($"Status request failed: {(int)statusResp.StatusCode} {statusResp.StatusCode} - {txt}");
        }

    using var stopContent = new System.Net.Http.StringContent("null", System.Text.Encoding.UTF8, "application/json");
    var stopResp = await client.PostAsync("/api/audio/stop", stopContent);
        if (!stopResp.IsSuccessStatusCode)
        {
            var txt = await stopResp.Content.ReadAsStringAsync();
            Assert.Fail($"Stop request failed: {(int)stopResp.StatusCode} {stopResp.StatusCode} - {txt}");
        }
    }
}
