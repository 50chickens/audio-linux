using System.CommandLine;
using System.CommandLine.Invocation;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var sshHost = new Option<string>("--ssh-host", description: "SSH host") { IsRequired = true };
        var sshPort = new Option<int>("--ssh-port", () => 22, description: "SSH port");
        var sshUser = new Option<string>("--ssh-user", description: "SSH user") { IsRequired = true };
        var sshKey = new Option<string>("--ssh-key", description: "Path to private key file") { IsRequired = true };
        var publishDir = new Option<string>("--publish-dir", () => "publish", description: "Publish directory to upload");
        var sshAutoConvert = new Option<bool>("--autoconvert-key", () => false, description: "Auto-convert OpenSSH private key to PEM in-memory for Renci.SshNet (no disk writes)");
        var generateSshKeys = new Option<string?>("--generatesshkeys", description: "Generate an RSA keypair and write files with the given prefix (default 'id_rsa')");
        var stdoutFlag = new Option<bool>(new[] { "-stdout", "--stdout" }, () => false, description: "Write keys to stdout instead of files (prints private then public)");

        var root = new RootCommand("Asionyx SSH bootstrap client") { sshHost, sshPort, sshUser, sshKey, publishDir, sshAutoConvert, generateSshKeys, stdoutFlag };

        root.SetHandler(async (InvocationContext ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(sshHost)!;
            var port = ctx.ParseResult.GetValueForOption(sshPort);
            var user = ctx.ParseResult.GetValueForOption(sshUser)!;
            var keyPath = ctx.ParseResult.GetValueForOption(sshKey)!;
            var pub = ctx.ParseResult.GetValueForOption(publishDir)!;

            if (!Directory.Exists(pub))
            {
                Console.Error.WriteLine($"Publish directory not found: {pub}");
                ctx.ExitCode = 2;
                return;
            }

            var autoConvert = ctx.ParseResult.GetValueForOption(sshAutoConvert);
            var genPrefix = ctx.ParseResult.GetValueForOption(generateSshKeys);
            var toStdout = ctx.ParseResult.GetValueForOption(stdoutFlag);

            if (!string.IsNullOrEmpty(genPrefix))
            {
                var prefix = genPrefix;
                if (string.IsNullOrEmpty(prefix)) prefix = "id_rsa";
                var (priv, pubKey) = KeyGenerator.GenerateRsaKeyPair();
                if (toStdout)
                {
                    // print private then public
                    Console.WriteLine(priv);
                    Console.WriteLine(pubKey);
                }
                else
                {
                    KeyGenerator.WriteKeyPairFiles(prefix, priv, pubKey);
                    Console.WriteLine($"Wrote private key: {prefix}");
                    Console.WriteLine($"Wrote public key: {prefix}.pub");
                }
                ctx.ExitCode = 0;
                return;
            }

            SshBootstrapper sb = new SshBootstrapper(host, user, keyPath, port);
            try
            {
                Console.WriteLine("Uploading publish directory...");
                sb.UploadDirectory(pub, $"/home/{user}/deployment-service");
                Console.WriteLine("Running install command...");
                var cmd = $"echo hello-from-bootstrap";
                var (exit, output, err) = sb.RunCommand(cmd);
                Console.WriteLine($"Exit={exit}, Output={output}, Error={err}");
                ctx.ExitCode = exit == 0 ? 0 : 1;
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Bootstrap failed: " + ex.Message);
                ctx.ExitCode = 2;
                return;
            }
        });

        return await root.InvokeAsync(args);
    }
}