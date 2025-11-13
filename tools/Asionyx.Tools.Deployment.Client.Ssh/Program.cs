using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

// Minimal Program: only startup and wiring. Application logic lives in SshCliRunner.
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Load configuration defaults from appsettings.json (project folder when running from source)
        // and also from the build output directory (AppContext.BaseDirectory) so both 'dotnet run' and
        // published/packaged runs will find configuration.
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var sshOpts = config.GetSection("Ssh").Get<SshOptions>() ?? new SshOptions();

        // Normalize KeyPath: expand %USERNAME% and ~
        sshOpts.KeyPath = ExpandPath(sshOpts.KeyPath);

        // CLI options: only keep those related to host verification or private-key testing.
        var generateSshKeys = new Option<string?>("--generatesshkeys", description: "Generate an RSA keypair and write files with the given prefix (default 'id_rsa')");
        var generateRetry = new Option<bool>(new[] { "-retry", "--retry" }, () => false, description: "When generating keys, optionally wait and attempt authentication in a loop (requires --ssh-host and --ssh-user and not using -stdout)");
        var stdoutFlag = new Option<bool>(new[] { "-stdout", "--stdout" }, () => false, description: "Write keys to stdout instead of files (prints private then public)");

        // verify-only: if present the client will only perform verification and will not do deployment activities
        var verifyOnly = new Option<bool>("--verify-only", () => false, description: "Run verification checks only; do not perform any deployment actions (preflight)");

        // verify/private-key related options
        var verifyPrivateKey = new Option<bool>("--verify-private-key", () => false, description: "Test whether the provided private key can authenticate to the remote host (tries raw key)");
        var verifyHostConfiguration = new Option<bool>("--verify-host-configuration", () => false, description: "Run quick remote checks (sudo, pwsh, dotnet) on the target host");
        var checkService = new Option<bool>("--check-service", () => false, description: "Check a systemd service status on the remote host (uses --service-name) -- treated as a verification step");

        // For verification we accept optional host/user/key overrides
        var sshHost = new Option<string?>("--ssh-host", description: "SSH host (override for verify/generate retry)");
        var sshPort = new Option<int?>("--ssh-port", description: "SSH port (override for verify)");
        var sshUser = new Option<string?>("--ssh-user", description: "SSH user (override for verify)");
        var sshKey = new Option<string?>("--ssh-key", description: "Path to private key file (override for verify)");
        var serviceName = new Option<string?>("--service-name", () => "deployment-service", description: "Name of the systemd service to check when using --check-service");

    var root = new RootCommand("Asionyx SSH bootstrap client") { generateSshKeys, generateRetry, stdoutFlag, verifyOnly, verifyPrivateKey, verifyHostConfiguration, checkService, serviceName, sshHost, sshPort, sshUser, sshKey };

        var runner = new SshCliRunner(sshOpts, config);

        // Use InvocationContext to avoid SetHandler overload limitations and bind options manually.
        root.SetHandler(async (InvocationContext ctx) =>
        {
            var genPrefix = ctx.ParseResult.GetValueForOption(generateSshKeys);
            var genRetry = ctx.ParseResult.GetValueForOption(generateRetry);
            var toStdout = ctx.ParseResult.GetValueForOption(stdoutFlag);
            var verify = ctx.ParseResult.GetValueForOption(verifyPrivateKey);
            var verifyHostCfg = ctx.ParseResult.GetValueForOption(verifyHostConfiguration);
            var chkService = ctx.ParseResult.GetValueForOption(checkService);
            var svcName = ctx.ParseResult.GetValueForOption(serviceName);
            var host = ctx.ParseResult.GetValueForOption(sshHost);
            var port = ctx.ParseResult.GetValueForOption(sshPort);
            var user = ctx.ParseResult.GetValueForOption(sshUser);
            var key = ctx.ParseResult.GetValueForOption(sshKey);
            

            var verifyOnlyFlag = ctx.ParseResult.GetValueForOption(verifyOnly);
            var clearJournalFlag = false; // no longer exposed via CLI

            // If --verify-only is requested, enable verification flags so the runner performs host and key checks.
            if (verifyOnlyFlag)
            {
                verify = true;
                verifyHostCfg = true;
            }

            // Determine which actions the runner should perform. When --verify-only is set, do not perform deployment activities.
            var performDeployment = !verifyOnlyFlag;

            // We still expose check-service as a verification-only operation.
            var result = await runner.HandleAsync(genPrefix, genRetry, toStdout, verify, verifyHostCfg, chkService, svcName, host, port, user, key, clearJournalFlag, performDeployment, performDeployment, performDeployment);
            ctx.ExitCode = result;
        });

        return await root.InvokeAsync(args);
    }

    private static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var p = path;
        // ~ expansion
        if (p.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            p = Path.Combine(home, p.TrimStart('~').TrimStart('/','\\'));
        }
        // %USERNAME% expansion (Windows style)
        if (p.Contains("%USERNAME%"))
        {
            var envUser = Environment.GetEnvironmentVariable("USERNAME") ?? Environment.UserName;
            p = p.Replace("%USERNAME%", envUser);
        }
        return p;
    }
}