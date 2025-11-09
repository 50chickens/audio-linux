using System.CommandLine;
using System.CommandLine.Invocation;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
    var sshHost = new Option<string>("--ssh-host", description: "SSH host");
        var sshPort = new Option<int>("--ssh-port", () => 22, description: "SSH port");
    var sshUser = new Option<string>("--ssh-user", description: "SSH user");
    var sshKey = new Option<string>("--ssh-key", () => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa"), description: "Path to private key file (defaults to ~/.ssh/id_rsa)");
        var publishDir = new Option<string>("--publish-dir", () => "publish", description: "Publish directory to upload");
        var sshAutoConvert = new Option<bool>("--autoconvert-key", () => false, description: "Auto-convert OpenSSH private key to PEM in-memory for Renci.SshNet (no disk writes)");
    var generateSshKeys = new Option<string?>("--generatesshkeys", description: "Generate an RSA keypair and write files with the given prefix (default 'id_rsa')");
    var generateRetry = new Option<bool>(new[] { "-retry", "--retry" }, () => false, description: "When generating keys, optionally wait and attempt authentication in a loop (requires --ssh-host and --ssh-user and not using -stdout)");
    var stdoutFlag = new Option<bool>(new[] { "-stdout", "--stdout" }, () => false, description: "Write keys to stdout instead of files (prints private then public)");
    var testPrivateKey = new Option<bool>(new[] { "-testprivatekey", "--test-private-key" }, () => false, description: "Test whether the provided private key can authenticate to the remote host (tries raw key then auto-convert)");

    var root = new RootCommand("Asionyx SSH bootstrap client") { sshHost, sshPort, sshUser, sshKey, publishDir, sshAutoConvert, generateSshKeys, generateRetry, stdoutFlag, testPrivateKey };

        root.SetHandler(async (InvocationContext ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(sshHost);
            var port = ctx.ParseResult.GetValueForOption(sshPort);
            var user = ctx.ParseResult.GetValueForOption(sshUser);
            var keyPath = ctx.ParseResult.GetValueForOption(sshKey);
            var pub = ctx.ParseResult.GetValueForOption(publishDir)!;

            var autoConvert = ctx.ParseResult.GetValueForOption(sshAutoConvert);
            var genPrefix = ctx.ParseResult.GetValueForOption(generateSshKeys);
            var genRetry = ctx.ParseResult.GetValueForOption(generateRetry);
            var toStdout = ctx.ParseResult.GetValueForOption(stdoutFlag);
            var doTestKey = ctx.ParseResult.GetValueForOption(testPrivateKey);

            // If we're running the publish/upload flow we need the publish dir.
            // But when generating keys or testing a private key, skip the publish-dir check.
            if (string.IsNullOrEmpty(genPrefix) && !toStdout && !doTestKey)
            {
                if (!Directory.Exists(pub))
                {
                    Console.Error.WriteLine($"Publish directory not found: {pub}");
                    ctx.ExitCode = 2;
                    return;
                }
            }

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
                // Also print a one-shot bash script the user can copy/paste into the remote host
                try
                {
                    var publicKeyLine = pubKey?.TrimEnd('\r', '\n') ?? string.Empty;
                    var script = $@"# Paste the following on the REMOTE HOST to add the public key to ~/.ssh/authorized_keys
mkdir -p ~/.ssh
chmod 700 ~/.ssh
cat >> ~/.ssh/authorized_keys <<'AUTHORIZED_KEYS'
{publicKeyLine}
AUTHORIZED_KEYS
chmod 600 ~/.ssh/authorized_keys
echo 'Public key installed'
";

                    Console.WriteLine();
                    Console.WriteLine("--- Remote install script (paste into remote host shell) ---");
                    Console.WriteLine(script);
                    Console.WriteLine("--- end script ---");
                }
                catch
                {
                    // best-effort: if printing the script fails, don't crash the tool
                }
                // If user requested waiting/testing, loop and attempt authentication until success or user aborts
                if (genRetry)
                {
                    if (toStdout)
                    {
                        Console.Error.WriteLine("Cannot use --wait when writing keys to stdout. Provide a file prefix instead.");
                        ctx.ExitCode = 2;
                        return;
                    }

                    if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user))
                    {
                        Console.Error.WriteLine("Error: --ssh-host and --ssh-user are required when using --retry to test generated keys.");
                        ctx.ExitCode = 2;
                        return;
                    }

                    var privatePath = System.IO.Path.GetFullPath(prefix);
                    Console.WriteLine($"Waiting to test generated keypair using private key file: {privatePath}");
                    while (true)
                    {
                        Console.WriteLine("Press Enter to attempt authentication with the generated key, or type 'q' then Enter to quit.");
                        var line = Console.ReadLine();
                        if (line != null && line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Aborting wait/test loop by user request.");
                            break;
                        }

                        try
                        {
                            var sbTest = new SshBootstrapper(host!, user!, privatePath, port, false);
                            var (exit, output, err) = sbTest.RunCommand("echo hello-asionyx-keytest");
                            if (exit == 0)
                            {
                                Console.WriteLine("Success: generated key authenticated successfully.");
                                break;
                            }
                            else
                            {
                                Console.WriteLine($"Authentication failed (exit={exit}). Error: {err}");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Print full diagnostics (stack trace and any inline diagnostics produced by SshBootstrapper)
                            Console.WriteLine("Authentication attempt threw:");
                            Console.WriteLine(ex.ToString());
                        }
                    }
                }
                ctx.ExitCode = 0;
                return;
            }

            // If the user asked only to test the private key, do that and exit
            if (doTestKey)
            {
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
                {
                    Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key are required for --test-private-key.");
                    ctx.ExitCode = 2;
                    return;
                }

                Console.WriteLine($"Testing private key file: {keyPath}");

                // First try raw key
                try
                {
                    var sbTest = new SshBootstrapper(host!, user!, keyPath!, port, false);
                    var (exit, output, err) = sbTest.RunCommand("echo hello-asionyx-keytest");
                    if (exit == 0)
                    {
                        Console.WriteLine($"Success: key file '{keyPath}' authenticated successfully (raw key).");
                        ctx.ExitCode = 0;
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Raw key test failed (exit={exit}). Error: {err}");
                    }
                }
                catch (Exception ex)
                {
                    // Print full exception (stack trace and any inner diagnostics)
                    Console.WriteLine("Raw key test threw:");
                    Console.WriteLine(ex.ToString());
                }

                // Try auto-convert
                try
                {
                    Console.WriteLine("Attempting auto-convert and retry...");
                    var sbConv = new SshBootstrapper(host!, user!, keyPath!, port, true);
                    var (exit2, output2, err2) = sbConv.RunCommand("echo hello-asionyx-keytest");
                    if (exit2 == 0)
                    {
                        Console.WriteLine($"Success: key authenticated after auto-convert (source file: '{keyPath}').");
                        ctx.ExitCode = 0;
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Auto-convert test failed (exit={exit2}). Error: {err2}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Auto-convert test threw:");
                    Console.WriteLine(ex.ToString());
                }

                Console.Error.WriteLine("Private key authentication failed (raw and auto-convert attempts). See messages above.");
                ctx.ExitCode = 1;
                return;
            }
            // When not generating keys, ensure required SSH parameters were provided
            if (string.IsNullOrEmpty(genPrefix))
            {
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
                {
                    Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key are required unless --generatesshkeys is used.");
                    ctx.ExitCode = 2;
                    return;
                }
            }

            SshBootstrapper sb = new SshBootstrapper(host!, user!, keyPath!, port, autoConvert);
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
                Console.Error.WriteLine("Bootstrap failed:");
                Console.Error.WriteLine(ex.ToString());
                ctx.ExitCode = 2;
                return;
            }
        });

        return await root.InvokeAsync(args);
    }
}