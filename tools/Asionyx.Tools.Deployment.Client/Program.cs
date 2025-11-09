using System.CommandLine;
using System.CommandLine.Invocation;

using System.Net.Http.Json;

var urlOption = new Option<string>("--deploy-url", () => "http://pistomp:5001", "Deployment service base URL");
var keyOption = new Option<string>("--key", () => "changeme", "API key");
var targetUrlOption = new Option<string>("--target-url", () => "http://pistomp:5000", "Target application base URL (for /info)");

var root = new RootCommand("Asionyx Deployment Client") { urlOption, keyOption, targetUrlOption };

var publishDirOption = new Option<string>("--publish-dir", () => "publish/deployment-service", "Local folder containing published deployment service to upload");
root.AddOption(publishDirOption);

// stop command
var stop = new Command("stop", "Stop the remote audio-router service")
{
};
stop.SetHandler(async (InvocationContext ctx) =>
{
    var deployUrl = ctx.ParseResult.GetValueForOption(urlOption);
    var key = ctx.ParseResult.GetValueForOption(keyOption);
    await PostServiceAction(deployUrl, key, "audio-router", "stop");
});

// start command
var start = new Command("start", "Start the remote audio-router service")
{
};
start.SetHandler(async (InvocationContext ctx) =>
{
    var deployUrl = ctx.ParseResult.GetValueForOption(urlOption);
    var key = ctx.ParseResult.GetValueForOption(keyOption);
    await PostServiceAction(deployUrl, key, "audio-router", "start");
});

// deploy command
var zipOption = new Option<string>("--zip", "Path to publish zip (required)");
var unitOption = new Option<string>("--unit", () => "deploy/audio-router.service", "Path to systemd unit file to install");
var deploy = new Command("deploy", "Upload and install a new audio-router publish zip") { zipOption, unitOption };
deploy.SetHandler(async (InvocationContext ctx) =>
{
    var deployUrl = ctx.ParseResult.GetValueForOption(urlOption)!;
    var key = ctx.ParseResult.GetValueForOption(keyOption)!;
    var zip = ctx.ParseResult.GetValueForOption(zipOption)!;
    var unit = ctx.ParseResult.GetValueForOption(unitOption)!;
    if (string.IsNullOrEmpty(zip) || !System.IO.File.Exists(zip)) { Console.Error.WriteLine("Zip file required and must exist"); return; }

    // Upload zip as multipart/form-data (metadata JSON + file)
    using var http = new HttpClient { BaseAddress = new Uri(deployUrl) };
    http.DefaultRequestHeaders.Add("X-Api-Key", key);

    using var content = new MultipartFormDataContent();
    var meta = new { TargetDir = "/opt/audio-router", FileName = System.IO.Path.GetFileName(zip) };
    var metaJson = System.Text.Json.JsonSerializer.Serialize(meta);
    content.Add(new StringContent(metaJson), "metadata");

    var stream = System.IO.File.OpenRead(zip);
    var fileContent = new StreamContent(stream);
    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
    content.Add(fileContent, "file", System.IO.Path.GetFileName(zip));

    var resp = await http.PostAsync("/api/files/upload", content);
    Console.WriteLine($"Upload response: {resp.StatusCode}");

    // Install unit
    var unitContent = System.IO.File.Exists(unit) ? System.IO.File.ReadAllText(unit) : "";
    var installPayload = new { Name = "audio-router", UnitFileContent = unitContent };
    var resp2 = await http.PostAsJsonAsync("/api/services/install", installPayload);
    Console.WriteLine($"Install response: {resp2.StatusCode}");
});

// info command (call audio-router /info endpoint)
var info = new Command("info", "Call the audio-router /info endpoint to verify provider and version")
{
};
info.SetHandler(async (InvocationContext ctx) =>
{
    var target = ctx.ParseResult.GetValueForOption(targetUrlOption);
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(target) };
        var r = await http.GetAsync("/info");
        var body = await r.Content.ReadAsStringAsync();
        Console.WriteLine($"Info: {r.StatusCode}\n{body}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Failed to call /info: " + ex.Message);
    }
});
static async Task PostServiceAction(string deployUrl, string apiKey, string name, string command)
{
    using var http = new HttpClient { BaseAddress = new Uri(deployUrl) };
    http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    var payload = new { Name = name, Command = command };
    var resp = await http.PostAsJsonAsync("/api/services/action", payload);
    Console.WriteLine($"Service action {command} returned {resp.StatusCode}");
}
