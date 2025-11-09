using NLog.Layouts;
using NLog.Targets;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// Read API key from environment variable DEPLOY_API_KEY or from command-line parameter --api-key
var configuredApiKey = Environment.GetEnvironmentVariable("DEPLOY_API_KEY");
if (string.IsNullOrWhiteSpace(configuredApiKey))
{
    configuredApiKey = builder.Configuration["api-key"];
}

if (string.IsNullOrWhiteSpace(configuredApiKey))
{
    // Fail fast: require API key to be provided either via DEPLOY_API_KEY env var or --api-key command line
    Console.Error.WriteLine("ERROR: DEPLOY_API_KEY environment variable or --api-key command line parameter must be provided to run this service.");
    return;
}

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
builder.Services.AddSingleton<Asionyx.Tools.Deployment.Services.IAuditStore, Asionyx.Tools.Deployment.Services.FileAuditStore>();
builder.Services.AddSingleton(new Asionyx.Tools.Deployment.Models.DeploymentOptions { ApiKey = configuredApiKey });

var app = builder.Build();

// API key middleware
app.UseMiddleware<Asionyx.Tools.Deployment.Middleware.ApiKeyAuthMiddleware>();

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

// Redirect root to the logviewer UI
app.MapGet("/", () => Results.Redirect("/logviewer/index.html"));

app.Run();
