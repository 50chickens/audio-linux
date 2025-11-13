using System.CommandLine;
using System.CommandLine.Invocation;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

// Build configuration from appsettings.json and environment
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Load defaults from appsettings
var appOpts = config.GetSection("App").Get<AppOptions>() ?? new AppOptions();

// Parse command-line overrides using CommandLineParser
var parseResult = Parser.Default.ParseArguments<CliOverrides>(args);
parseResult.WithParsed(ov =>
{
    if (!string.IsNullOrEmpty(ov.DeployUrl)) appOpts.DeployUrl = ov.DeployUrl!;
    if (!string.IsNullOrEmpty(ov.ApiKey)) appOpts.ApiKey = ov.ApiKey!;
    if (!string.IsNullOrEmpty(ov.TargetUrl)) appOpts.TargetUrl = ov.TargetUrl!;
    if (!string.IsNullOrEmpty(ov.PublishDir)) appOpts.PublishDir = ov.PublishDir!;
});

// Register options into DI
var services = new ServiceCollection();
services.AddSingleton(appOpts);
var serviceProvider = services.BuildServiceProvider();

var urlOption = new Option<string>("--deploy-url", description: "Deployment service base URL");
var keyOption = new Option<string>("--key", description: "API key");
var targetUrlOption = new Option<string>("--target-url", description: "Target application base URL (for /info)");

var root = new RootCommand("Asionyx Deployment Client") { urlOption, keyOption, targetUrlOption };

var publishDirOption = new Option<string>("--publish-dir", description: "Local folder containing published deployment service to upload");
root.AddOption(publishDirOption);

// stop command
var stop = new Command("stop", "Stop the remote audio-router service")
{
};
stop.SetHandler(async (InvocationContext ctx) =>
{
    var opts = serviceProvider.GetRequiredService<AppOptions>();
    var deployUrl = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(urlOption)) ? ctx.ParseResult.GetValueForOption(urlOption) : opts.DeployUrl;
    var key = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(keyOption)) ? ctx.ParseResult.GetValueForOption(keyOption) : opts.ApiKey;
    await PostServiceAction(deployUrl, key, "audio-router", "stop");
});

// start command
var start = new Command("start", "Start the remote audio-router service")
{
};
start.SetHandler(async (InvocationContext ctx) =>
{
    var opts = serviceProvider.GetRequiredService<AppOptions>();
    var deployUrl = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(urlOption)) ? ctx.ParseResult.GetValueForOption(urlOption) : opts.DeployUrl;
    var key = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(keyOption)) ? ctx.ParseResult.GetValueForOption(keyOption) : opts.ApiKey;
    await PostServiceAction(deployUrl, key, "audio-router", "start");
});

// deploy command
var zipOption = new Option<string>("--zip", "Path to publish zip (required)");
var unitOption = new Option<string>("--unit", description: "Path to systemd unit file to install");
var deploy = new Command("deploy", "Upload and install a new audio-router publish zip") { zipOption, unitOption };
deploy.SetHandler(async (InvocationContext ctx) =>
{
    var opts = serviceProvider.GetRequiredService<AppOptions>();
    var deployUrl = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(urlOption)) ? ctx.ParseResult.GetValueForOption(urlOption) : opts.DeployUrl;
    var key = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(keyOption)) ? ctx.ParseResult.GetValueForOption(keyOption) : opts.ApiKey;
    var zip = ctx.ParseResult.GetValueForOption(zipOption)!;
    var unit = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(unitOption)) ? ctx.ParseResult.GetValueForOption(unitOption) : opts.UnitFile;
    if (string.IsNullOrEmpty(zip) || !System.IO.File.Exists(zip)) { Console.Error.WriteLine("Zip file required and must exist"); return; }

    // Upload zip as multipart/form-data (metadata JSON + file)
    using var http = new HttpClient { BaseAddress = new Uri(deployUrl) };
    http.DefaultRequestHeaders.Add("X-Api-Key", key);

    using var content = new MultipartFormDataContent();
    var meta = new { TargetDir = opts.TargetDir, FileName = System.IO.Path.GetFileName(zip) };
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
    var opts = serviceProvider.GetRequiredService<AppOptions>();
    var target = !string.IsNullOrEmpty(ctx.ParseResult.GetValueForOption(targetUrlOption)) ? ctx.ParseResult.GetValueForOption(targetUrlOption) : opts.TargetUrl;
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
