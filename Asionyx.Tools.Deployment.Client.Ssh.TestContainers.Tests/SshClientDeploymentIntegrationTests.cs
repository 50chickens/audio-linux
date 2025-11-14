using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Testcontainers.Sshd;
using System.Runtime.InteropServices;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

[TestFixture]
[Category("RequiresHost")]
public class SshClientDeploymentIntegrationTests
{
    [SetUp]
    public void SkipIfWindowsHost()
    {
        // Use capability-based skipping so tests can run when a Docker host (local or remote Linux) is available.
        RequiresHostHelper.EnsureHostOrIgnore();
    }

    [Test]
    public static async Task Deploy_Server_PublishesAndUploadsAndFileExists()
    {
        // Prefer using the pre-published server output (published by the test project's MSBuild Target)
        var repoPublishDir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "publish", "Asionyx.Service.Deployment.Linux"));
        string publishDir;
        if (Directory.Exists(repoPublishDir) && File.Exists(Path.Combine(repoPublishDir, "Asionyx.Service.Deployment.Linux.dll")))
        {
            publishDir = repoPublishDir;
            TestContext.WriteLine($"Using repo-published server output at: {publishDir}");
        }
        else
        {
            // Fallback: publish to a temp folder (maintains original behavior when running tests without MSBuild-driven publish)
            publishDir = Path.Combine(Path.GetTempPath(), "asionyx_publish_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(publishDir);

            var projectPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src", "Asionyx.Service.Deployment.Linux", "Asionyx.Service.Deployment.Linux.csproj");
            projectPath = Path.GetFullPath(projectPath);

            var psi = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -c Debug -o \"{publishDir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                Assert.That(p, Is.Not.Null, "dotnet publish process failed to start");
                var sout = await p.StandardOutput.ReadToEndAsync();
                var serr = await p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                Assert.That(p.ExitCode, Is.EqualTo(0), () => $"dotnet publish failed. STDOUT:\n{sout}\nSTDERR:\n{serr}");
            }
        }

        // Generate RSA keypair and write to temp file
        var keyGen = new Org.BouncyCastle.Crypto.Generators.RsaKeyPairGenerator();
        keyGen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();
        string privatePem;
        using (var sw = new StringWriter())
        {
            var pw = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pw.WriteObject(keyPair.Private);
            pw.Writer.Flush();
            privatePem = sw.ToString();
        }

        var hostKeyPath = Path.Combine(Path.GetTempPath(), $"ssh_test_key_{Guid.NewGuid():N}");
        File.WriteAllText(hostKeyPath, privatePem, Encoding.ASCII);

    // Set DOCKER_HOST for this test process so tests are self-contained (restored in finally)
    var prevDockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
    Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://localhost:2375", EnvironmentVariableTarget.Process);

    try
    {
                var username = "pistomp";

                // Per-run API key so the deployment app inside the image uses a known key
                var apiKey = Guid.NewGuid().ToString("N");

                await using var container = new SshdBuilder()
                    .WithImage("audio-linux/ci-systemd-trixie:local")
                    .WithEnvironment("API_KEY", apiKey)
                    // Bind the host cgroup filesystem into the container to help systemd boot in non-privileged environments
                    .WithBindMount("/sys/fs/cgroup", "/sys/fs/cgroup")
                    .WithTestUserSetup(username)
                    .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys")
                    .Build();

                await container.StartAsync(CancellationToken.None);

                var host = container.Hostname;
                long sshPortRaw = container.GetMappedPublicPort(SshdBuilder.SshdPort);
                int sshPort = checked((int)sshPortRaw);

                // Expose helper to run commands as root inside the container (ExecAsync runs as root)
                async Task<(int ExitCode, string StdOut, string StdErr)> ExecRoot(string cmd)
                {
                    var res = await container.ExecAsync(new[] { "/bin/sh", "-c", cmd }, CancellationToken.None).ConfigureAwait(false);
                    return (checked((int)res.ExitCode), res.Stdout ?? string.Empty, res.Stderr ?? string.Empty);
                }

                // Make sure the target container is capable of running systemd + dotnet.
                // If not present, skip the test (this integration requires an image with systemd and dotnet runtime installed).
                var (sysExit, sysOut, sysErr) = await ExecRoot("which systemctl || true");
                var (dotExit, dotOut, dotErr) = await ExecRoot("which dotnet || true");
                if (string.IsNullOrWhiteSpace(sysOut) || string.IsNullOrWhiteSpace(dotOut))
                {
                    Assert.Fail("Test requires a target image with systemd and dotnet installed; image is missing required runtime or systemd.");
                }

                // Ensure the new non-root user has passwordless sudo so deployment can use sudo non-interactively
                var (sudoExit, sudoOut, sudoErr) = await ExecRoot($"echo '{username} ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/{username} && chmod 440 /etc/sudoers.d/{username}");
                Assert.That(sudoExit, Is.EqualTo(0), () => $"Failed to configure sudoers for {username}. out:{sudoOut} err:{sudoErr}");

                // Prepare remote deploy directory under /opt
                var remoteDeployDir = "/opt/Asionyx.Service.Deployment.Linux";
                var (mkOptExit, mkOptOut, mkOptErr) = await ExecRoot($"mkdir -p {remoteDeployDir} && chown -R {username}:{username} {remoteDeployDir} && chmod 755 {remoteDeployDir}");
                Assert.That(mkOptExit, Is.EqualTo(0), () => $"Failed to create /opt deploy dir. out:{mkOptOut} err:{mkOptErr}");

                // Use SshBootstrapper to upload the published release into /opt
                var sb = new SshBootstrapper(host, username, hostKeyPath, sshPort);
                sb.UploadDirectory(host, sshPort, username, hostKeyPath, publishDir, remoteDeployDir);

                // Quick check that the main DLL exists
                using (var fs = File.OpenRead(hostKeyPath))
                {
                    var pk = new Renci.SshNet.PrivateKeyFile(fs);
                    var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                    var conn = new Renci.SshNet.ConnectionInfo(host, sshPort, username, keyAuth);
                    using var client = new Renci.SshNet.SshClient(conn);
                    client.Connect();
                    var checkCmd = client.RunCommand($"test -f {remoteDeployDir}/Asionyx.Service.Deployment.Linux.dll && echo exists || echo missing");
                    Assert.That(checkCmd.ExitStatus, Is.EqualTo(0));
                    var result = checkCmd.Result.Trim();
                    Assert.That(result, Is.EqualTo("exists"), "Uploaded dll not found on remote container");
                    client.Disconnect();
                }

                // Now invoke the SSH client runner to perform the full deployment flow (this mirrors build-deploy-and-run.ps1 usage)
                var options = new SshOptions
                {
                    Host = host,
                    Port = sshPort,
                    User = username,
                    KeyPath = hostKeyPath,
                    PublishDir = publishDir
                };

                var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
                var runner = new SshCliRunner(options, config);

                var rc = await runner.HandleAsync(genPrefix: null,
                                                  genRetry: false,
                                                  toStdout: false,
                                                  doVerify: false,
                                                  verifyHostConfig: false,
                                                  checkService: false,
                                                  serviceName: null,
                                                  hostOverride: null,
                                                  portOverride: null,
                                                  userOverride: null,
                                                  keyOverride: null,
                                                  clearJournal: false,
                                                  ensureRemoteDir: false,
                                                  ensureUserDataDir: false,
                                                  installSystemdUnit: false);
                Assert.That(rc, Is.EqualTo(0), "SshCliRunner deployment flow failed (non-zero exit)");

                // Verify the systemd service is active
                var (svcExit, svcOut, svcErr) = await ExecRoot("systemctl is-active deployment-service || true");
                Assert.That(svcExit, Is.EqualTo(0), () => $"systemctl reported non-zero while checking service. out:{svcOut} err:{svcErr}");
                Assert.That((svcOut ?? string.Empty).Trim(), Is.EqualTo("active"), "deployment-service is not active after installation");

                // Query the /status endpoint from inside the container using curl (or pwsh fallback)
                var (curlExit, curlOut, curlErr) = await ExecRoot("curl -sS http://localhost:5001/status || true");
                if (string.IsNullOrWhiteSpace(curlOut))
                {
                    // try pwsh Invoke-RestMethod if curl missing
                    var (pwExit, pwOut, pwErr) = await ExecRoot("pwsh -NoProfile -NonInteractive -EncodedCommand $([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(\"try { $r = Invoke-RestMethod -Uri 'http://localhost:5001/status'; $r | ConvertTo-Json -Compress } catch { Write-Output 'invoke-failed'; exit 4 }\"))) || true");
                        Assert.That(pwExit, Is.EqualTo(0), () => $"Failed to query /status with pwsh. out:{pwOut} err:{pwErr}");
                        Assert.That(pwOut, Is.Not.Null.And.Not.EqualTo(string.Empty), "/status returned empty payload (pwsh)");
                }
                else
                {
                        Assert.That(curlOut, Is.Not.Null.And.Not.EqualTo(string.Empty), "/status returned empty payload (curl)");
                }
        }
        finally
        {
            try { File.Delete(hostKeyPath); } catch { }
            try { Directory.Delete(publishDir, true); } catch { }
            // restore DOCKER_HOST
            Environment.SetEnvironmentVariable("DOCKER_HOST", prevDockerHost, EnvironmentVariableTarget.Process);
        }
    }
}
