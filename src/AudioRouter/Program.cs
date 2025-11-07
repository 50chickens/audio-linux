using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Layouts;
using NLog.Targets;
using NLog.Web;
using AudioRouter.Services;
using AudioRouter.Library.Core;
using AudioRouter.Library.Audio.JackSharp;
using AudioRouter.Library.Audio.SoundFlow;

var builder = Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Configure NLog programmatically to emit JSON log entries (match deployment service shape)
var nlogConfig = new NLog.Config.LoggingConfiguration();

var jsonLayout = new JsonLayout { IncludeAllProperties = false };
jsonLayout.Attributes.Add(new JsonAttribute("datestamp", "${longdate}"));
jsonLayout.Attributes.Add(new JsonAttribute("Level", "${level:lowercase=true}"));
jsonLayout.Attributes.Add(new JsonAttribute("E", "${logger}"));

var payloadLayout = new JsonLayout { IncludeAllProperties = false };
payloadLayout.Attributes.Add(new JsonAttribute("Message", "${message}"));
jsonLayout.Attributes.Add(new JsonAttribute("payload", payloadLayout));

// Ensure local logs directory exists
var localLogDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "logs");
try { System.IO.Directory.CreateDirectory(localLogDir); } catch { /* best-effort */ }

var logFile = new FileTarget("logfile")
{
    FileName = "logs/audio-activities-${date:format=dddd}.log",
    Layout = jsonLayout,
    ConcurrentWrites = true,
    KeepFileOpen = false
};

var consoleTarget = new ConsoleTarget("console") { Layout = "${message}" };

nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, consoleTarget);
nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, logFile);

NLog.LogManager.Configuration = nlogConfig;

// Use NLog as the logging provider
builder.ConfigureLogging(logging => logging.ClearProviders());
builder.UseNLog()
    .ConfigureWebHostDefaults(webHost =>
    {
        webHost.ConfigureKestrel(options =>
        {
            // No HTTPS bindings; Kestrel default HTTP on :5000
        });

        webHost.ConfigureServices((ctx, services) =>
        {
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
        });

        webHost.Configure((ctx, app) =>
        {
            if (ctx.HostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        });
    })
    .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        // Choose provider via command-line: --provider JackSharpCore (default) or --provider SoundFlow
        var provider = context.Configuration["provider"] ?? context.Configuration["Provider"] ?? "JackSharpCore";

        // Register application services based on selected provider
        if (string.Equals(provider, "SoundFlow", StringComparison.OrdinalIgnoreCase))
        {
            containerBuilder.RegisterType<AudioRouter.Library.Audio.SoundFlow.SoundFlowAudioRouter>().As<AudioRouter.Library.Core.IAudioRouter>().SingleInstance();
        }
        else
        {
            containerBuilder.RegisterType<AudioRouter.Library.Audio.JackSharp.JackAudioRouter>().As<AudioRouter.Library.Core.IAudioRouter>().SingleInstance();
        }
    });

var host = builder.Build();
await host.RunAsync();

// Public partial Program class for integration tests (WebApplicationFactory<Program>)
public partial class Program { }
