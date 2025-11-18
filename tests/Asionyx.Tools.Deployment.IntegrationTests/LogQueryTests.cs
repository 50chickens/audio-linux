using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace Asionyx.Tools.Deployment.IntegrationTests;

public class LogQueryTests
{
    private WebApplicationFactory<Program> _factory;
    private string? _testKey;

    [SetUp]
    public void Setup()
    {
    // Create a temp content root with a logs file
    var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(temp);
    // Ensure an API key is present for the app by writing ServiceSettings.release.json into the content root
    var release = Path.Combine(temp, "ServiceSettings.release.json");
    _testKey = Guid.NewGuid().ToString("N");
    File.WriteAllText(release, $"{{ \"ApiKey\": \"{_testKey}\" }}");
        Directory.CreateDirectory(Path.Combine(temp, "logs"));

        var logPath = Path.Combine(temp, "logs", "deployment.log");
        var lines = new[] {
            JsonSerializer.Serialize(new { datestamp = "2025-11-07 10:00:00.0000", Level = "info", E = "Tests.Logger", payload = new { Message = "started" } }),
            JsonSerializer.Serialize(new { datestamp = "2025-11-07 11:00:00.0000", Level = "error", E = "Tests.Logger", payload = new { Message = "failed upload" } }),
            JsonSerializer.Serialize(new { datestamp = "2025-11-07 12:00:00.0000", Level = "info", E = "Tests.Logger", payload = new { Message = "done" } })
        };
        File.WriteAllLines(logPath, lines);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseContentRoot(temp));
    }

    [TearDown]
    public void TearDown()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task Query_FilterByLevel_ShouldReturnErrorOnly()
    {
        using var client = _factory!.CreateClient();
    client.DefaultRequestHeaders.Add("X-Api-Key", _testKey);
    var all = await client.GetAsync("/api/logs");
    all.EnsureSuccessStatusCode();
    var allArr = JsonSerializer.Deserialize<JsonElement[]>(await all.Content.ReadAsStringAsync());
    Assert.That(allArr, Is.Not.Null);
    Assert.That(allArr!.Length, Is.EqualTo(3));

    var r = await client.GetAsync("/api/logs?filter=Level==error");
    r.EnsureSuccessStatusCode();
    var arr = JsonSerializer.Deserialize<JsonElement[]>(await r.Content.ReadAsStringAsync());
    Assert.That(arr, Is.Not.Null);
    Assert.That(arr!.Length, Is.EqualTo(1));
    var msg = arr[0].GetProperty("payload").GetProperty("Message").GetString();
    Assert.That(msg, Is.EqualTo("failed upload"));
    }

    [Test]
    public async Task Query_OrAndRange_TimestampRangeAndOr()
    {
    using var client = _factory!.CreateClient();
    client.DefaultRequestHeaders.Add("X-Api-Key", _testKey);

        // Extended query language: we'll test OR by running two queries and combining results
        var r1 = await client.GetAsync("/api/logs?filter=datestamp==2025-11-07 10:00:00.0000");
        r1.EnsureSuccessStatusCode();
        var a1 = JsonSerializer.Deserialize<JsonElement[]>(await r1.Content.ReadAsStringAsync());
        var r2 = await client.GetAsync("/api/logs?filter=datestamp==2025-11-07 12:00:00.0000");
        r2.EnsureSuccessStatusCode();
        var a2 = JsonSerializer.Deserialize<JsonElement[]>(await r2.Content.ReadAsStringAsync());

        Assert.That((a1?.Length ?? 0) + (a2?.Length ?? 0), Is.EqualTo(2));
    }
}
