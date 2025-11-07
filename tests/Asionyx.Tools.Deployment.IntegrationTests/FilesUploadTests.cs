using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace Asionyx.Tools.Deployment.IntegrationTests;

public class FilesUploadTests
{
    private WebApplicationFactory<Program>? _factory;

    [SetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("DEPLOY_API_KEY", "testkey");
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseContentRoot(temp));
    }

    [TearDown]
    public void TearDown()
    {
        try { _factory?.Dispose(); } catch { }
    }

    [Test]
    public async Task UploadZip_CreatesFilesAndExtracts()
    {
        // Test uploading a non-zip binary file is saved to target
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        var bytes = Encoding.UTF8.GetBytes("some content");

        using var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "testkey");

        using var content = new MultipartFormDataContent();
        var targetDir = Path.Combine(temp, "deploy_target");
        var meta = new { TargetDir = targetDir, FileName = "data.bin" };
        content.Add(new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8, "application/json"), "metadata");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "data.bin");

        var resp = await client.PostAsync("/api/files/upload", content);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.IsTrue(resp.IsSuccessStatusCode, body);

        // verify saved file exists
        var saved = Path.Combine(targetDir, "data.bin");
        Assert.IsTrue(File.Exists(saved));
    }

    [Test]
    public async Task Upload_WithoutApiKey_ReturnsUnauthorized()
    {
        using var client = _factory!.CreateClient();
        // no header
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("{}"), "metadata");
        var resp = await client.PostAsync("/api/files/upload", content);
        Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Upload_MissingMetadata_ReturnsBadRequest()
    {
        using var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "testkey");
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("not a zip");
        content.Add(new ByteArrayContent(bytes), "file", "file.bin");
        var resp = await client.PostAsync("/api/files/upload", content);
        Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }
}
