using NLog.Layouts;
using NLog.Targets;
using NLog.Web;
using Asionyx.Service.Deployment.Shared;

var builder = WebApplication.CreateBuilder(args);

// Configuration file load order requirements (explicit):
// - ServiceSettings.json (optional; don't store values here)
// - ServiceSettings.local.json (optional)
// - ServiceSettings.release.json (mandatory)
// and for appsettings.json:
// - appsettings.json (optional)
// - appsettings.local.json (optional)
// - appsettings.release.json (mandatory)
// Add those sources in order so later files override earlier ones.
builder.Configuration.AddJsonFile("ServiceSettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("ServiceSettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("ServiceSettings.release.json", optional: true, reloadOnChange: true);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.release.json", optional: true, reloadOnChange: true);

// The desired data folder can still be configured under Service:DataFolder in configuration (default: ~/.Asionyx.Service.Deployment.Linux)
// Read the configured data folder (may include a '~')
var configuredDataFolder = builder.Configuration["Service:DataFolder"] ?? "~/.Asionyx.Service.Deployment.Linux";

// Expand ~ to the current user's home directory (at runtime this will be the effective user's home; when running under systemd as the service user this will be that user's home)
static string ExpandTilde(string path)
{
    if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;
    if (path.StartsWith("~"))
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, path.TrimStart('~').TrimStart('/', '\\'));
    }
    return path;
}

var dataFolderExpanded = ExpandTilde(configuredDataFolder);

// Bind ServiceSettings from configuration
var serviceSettings = new Asionyx.Service.Deployment.Linux.Models.ServiceSettings();
builder.Configuration.Bind(serviceSettings);
// If DataFolder not set in settings (but present under Service:DataFolder), copy it
if (string.IsNullOrWhiteSpace(serviceSettings.DataFolder))
{
    serviceSettings.DataFolder = configuredDataFolder;
}

// Ensure the expanded data folder path is available for later use
var dataFolderPath = ExpandTilde(serviceSettings.DataFolder);

// Read API key from ServiceSettings (if present) or legacy appsettings key
// Create an ApiKeyResolver that prefers the value in ServiceSettings, and if absent
// generates and persists a new key once.
var apiKeyLock = new object();
ApiKeyResolver apiKeyResolver = () =>
{
    if (!string.IsNullOrWhiteSpace(serviceSettings.ApiKey)) return serviceSettings.ApiKey!;
    lock (apiKeyLock)
    {
        if (!string.IsNullOrWhiteSpace(serviceSettings.ApiKey)) return serviceSettings.ApiKey!;
        var generated = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        serviceSettings.ApiKey = generated;
        try
        {
            var contentRoot = builder.Environment.ContentRootPath ?? System.IO.Directory.GetCurrentDirectory();
            var releaseSettingsPath = System.IO.Path.Combine(contentRoot, "ServiceSettings.release.json");

            System.Text.Json.Nodes.JsonObject root;
            if (System.IO.File.Exists(releaseSettingsPath))
            {
                var txt = System.IO.File.ReadAllText(releaseSettingsPath);
                var parsed = System.Text.Json.Nodes.JsonNode.Parse(txt);
                root = parsed as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                root = new System.Text.Json.Nodes.JsonObject();
            }

            root["ApiKey"] = generated;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(releaseSettingsPath, root.ToJsonString(opts));
            Console.WriteLine($"Generated new API key and wrote to {releaseSettingsPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to persist new API key to ServiceSettings.release.json: {ex.Message}");
        }

        return serviceSettings.ApiKey!;
    }
};

// Ensure the resolver is registered and we obtain the current key for any legacy consumers
var configuredApiKey = apiKeyResolver();

// Run deployment service on a dedicated management port to avoid colliding with other apps
builder.WebHost.UseUrls("http://*:5001");

// Configure NLog programmatically to emit JSON log entries with the required shape.
// Each log line will be a JSON object:
// { "datestamp": "...", "Level": "info", "E": "Namespace.Class", "payload": { "Message": "..." } }
var nlogConfig = new NLog.Config.LoggingConfiguration();

var jsonLayout = new JsonLayout { IncludeEventProperties = false };
jsonLayout.Attributes.Add(new JsonAttribute("datestamp", "${longdate}"));
jsonLayout.Attributes.Add(new JsonAttribute("Level", "${level:lowercase=true}"));
jsonLayout.Attributes.Add(new JsonAttribute("E", "${logger}"));

var payloadLayout = new JsonLayout { IncludeEventProperties = false };
payloadLayout.Attributes.Add(new JsonAttribute("Message", "${message}"));
jsonLayout.Attributes.Add(new JsonAttribute("payload", payloadLayout));

var logFile = new FileTarget("logfile")
{
    // Use weekday-only file names so the same file is reused each week (e.g. deployment-Monday.log)
    FileName = "logs/deployment-${date:format=dddd}.log",
    Layout = jsonLayout,
    KeepFileOpen = false
};

var consoleTarget = new ConsoleTarget("console") { Layout = "${message}" };

nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, consoleTarget);
nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, logFile);

NLog.LogManager.Configuration = nlogConfig;

// Replace default logging providers and use NLog
builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration: API key
// Register service settings and deployment options
builder.Services.AddSingleton(serviceSettings);
builder.Services.AddSingleton<Asionyx.Service.Deployment.Linux.Services.IAuditStore, Asionyx.Service.Deployment.Linux.Services.FileAuditStore>();
builder.Services.AddSingleton(new Asionyx.Service.Deployment.Linux.Models.DeploymentOptions { ApiKey = configuredApiKey });
// Register the ApiKey resolver so consumers (and tests) can resolve the current API key via DI.
builder.Services.AddApiKeyResolver(apiKeyResolver);

var app = builder.Build();

// API key middleware (can be disabled by setting DISABLE_API_KEY=1 in the environment)
var disableApi = Environment.GetEnvironmentVariable("DISABLE_API_KEY");
if (string.IsNullOrWhiteSpace(disableApi))
{
    app.UseMiddleware<Asionyx.Service.Deployment.Linux.Middleware.ApiKeyAuthMiddleware>();
}
else
{
    Console.WriteLine("Warning: API key authentication disabled via DISABLE_API_KEY environment variable");
}

// simple health endpoint (no API key required)
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Serve a simple log viewer UI from wwwroot/logviewer
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// Unauthenticated status endpoint
app.MapGet("/status", () =>
{
    // Hostname
    var hostname = System.Net.Dns.GetHostName();
    // IP addresses (non-loopback IPv4)
    var addrs = System.Net.Dns.GetHostEntry(hostname).AddressList
        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))
        .Select(a => a.ToString())
        .ToArray();
    // Application version
    var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty).ProductVersion ?? "unknown";
    return Results.Ok(new { hostname, ipAddresses = addrs, version = ver });
});

// Redirect root to the logviewer UI
app.MapGet("/", () => Results.Redirect("/logviewer/index.html"));

app.Run();
