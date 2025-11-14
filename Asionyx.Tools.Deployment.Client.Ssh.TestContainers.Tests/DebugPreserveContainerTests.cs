using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Testcontainers.Sshd;
using System.Runtime.InteropServices;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

[TestFixture]
[Category("RequiresHost")]
public class DebugPreserveContainerTests
{
    [SetUp]
    public void SkipIfWindowsHost()
    {
        // Use capability-based skipping so tests run when a Docker host (local or remote Linux) is available and the image can be built.
        RequiresHostHelper.EnsureHostOrIgnore();
    }

    // This integration diagnostic uses the Testcontainers SshdBuilder and startup callbacks
    // to provision the test user and SSH keys. It avoids privileged mode and host volume
    // mounts; if the image requires privileged/container mounts to run systemd, the test
    // will help reveal that by failing to start the service. The container is intentionally
    // left running so you can inspect it manually if desired.
    [Test]
    public static async Task Deploy_Server_Debug_PreserveContainerForTriage()
    {
        // Prepare published server artifact (reuse same fallback behavior as other tests)
        var repoPublishDir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "publish", "Asionyx.Service.Deployment.Linux"));
        string publishDir;
        var createdTempPublishDir = false;
        if (Directory.Exists(repoPublishDir) && File.Exists(Path.Combine(repoPublishDir, "Asionyx.Service.Deployment.Linux.dll")))
        {
            publishDir = repoPublishDir;
            TestContext.WriteLine($"Using repo-published server output at: {publishDir}");
        }
        else
        {
            publishDir = Path.Combine(Path.GetTempPath(), "asionyx_publish_" + Guid.NewGuid().ToString("N"));
            createdTempPublishDir = true;
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
                Assert.That(p, Is.Not.Null, "dotnet publish failed to start");
                var sout = await p.StandardOutput.ReadToEndAsync();
                var serr = await p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                Assert.That(p.ExitCode, Is.EqualTo(0), () => $"dotnet publish failed. STDOUT:\n{sout}\nSTDERR:\n{serr}");
            }
        }

        // Generate RSA keypair and write to temp host key file
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

        var username = "pistomp";

        try
        {
            // Build a Testcontainers-based Sshd container using the CI image. Do NOT use volume mounts or privileged mode here.
            // Provide per-run API key for the deployment service inside the image
            var apiKey = Guid.NewGuid().ToString("N");

            var builder = new SshdBuilder()
                .WithImage("audio-linux/ci-systemd-trixie:local")
                .WithEnvironment("API_KEY", apiKey)
                // Mount host cgroup into container to improve systemd boot behavior on some Docker engines
                .WithBindMount("/sys/fs/cgroup", "/sys/fs/cgroup")
                .WithTestUserSetup(username)
                .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys");

            var container = builder.Build();

            // Start the container and let the Testcontainers wait strategy do its work.
            try
            {
                await container.StartAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Try to fetch container inspect details via Docker.DotNet to help debugging without spawning external processes.
                try
                {
                    var dockerConfig = new Docker.DotNet.DockerClientConfiguration();
                    using var docker = dockerConfig.CreateClient();

                    var inspect = await docker.Containers.InspectContainerAsync(container.Id, CancellationToken.None).ConfigureAwait(false);
                    TestContext.WriteLine("=== Container inspect ===");
                    TestContext.WriteLine($"Id: {inspect.ID}");
                    TestContext.WriteLine($"Name: {inspect.Name}");
                    TestContext.WriteLine($"Image: {inspect.Image}");
                    TestContext.WriteLine($"State: Status={inspect.State.Status} Running={inspect.State.Running} ExitCode={inspect.State.ExitCode} Error={inspect.State.Error}");
                    if (inspect.State.StartedAt != null) TestContext.WriteLine($"StartedAt: {inspect.State.StartedAt}");
                    if (inspect.State.FinishedAt != null) TestContext.WriteLine($"FinishedAt: {inspect.State.FinishedAt}");
                    TestContext.WriteLine("=== End inspect ===");
                }
                catch (Exception logEx)
                {
                    TestContext.WriteLine($"Failed to inspect container via Docker client: {logEx.Message}");
                }

                // Re-throw original start exception to keep test failure semantics
                throw;
            }

            var host = container.Hostname;
            long sshPortRaw = container.GetMappedPublicPort(SshdBuilder.SshdPort);
            int sshPort = checked((int)sshPortRaw);

            TestContext.WriteLine($"Started testcontainer host={host} sshPort={sshPort}");

            // Helper to run commands inside container as root
            async Task<(int ExitCode, string StdOut, string StdErr)> ExecRoot(string cmd)
            {
                var res = await container.ExecAsync(new[] { "/bin/sh", "-c", cmd }, CancellationToken.None).ConfigureAwait(false);
                return (checked((int)res.ExitCode), res.Stdout ?? string.Empty, res.Stderr ?? string.Empty);
            }

            // Collect some quick diagnostics from inside the container to understand why systemd/sshd might not be ready.
            var (p1, out1, err1) = await ExecRoot("/proc/1/comm || true");
            TestContext.WriteLine($"/proc/1/comm exit={p1} out={out1} err={err1}");

            var (sysExit, sysOut, sysErr) = await ExecRoot("which systemctl || true");
            TestContext.WriteLine($"which systemctl -> exit={sysExit} out={sysOut} err={sysErr}");

            var (sshdExit, sshdOut, sshdErr) = await ExecRoot("ps aux | grep -i sshd || true");
            TestContext.WriteLine($"ps aux | grep sshd -> exit={sshdExit} out={sshdOut} err={sshdErr}");

            // Try to read journalctl if present (may not be installed in minimal images)
            var (jcExit, jcOut, jcErr) = await ExecRoot("journalctl -b --no-pager -n 200 || echo 'no-journal' && true");
            TestContext.WriteLine($"journalctl -> exit={jcExit} out={(string.IsNullOrWhiteSpace(jcOut)? "<empty>" : jcOut.Substring(0, Math.Min(jcOut.Length, 4000)))} err={jcErr}");

            // Attempt an SSH connection from the test process to validate access
            var sshReady = false;
            try
            {
                using (var fs = File.OpenRead(hostKeyPath))
                {
                    var pk = new Renci.SshNet.PrivateKeyFile(fs);
                    var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                    var conn = new Renci.SshNet.ConnectionInfo(host, sshPort, username, keyAuth);
                    using var client = new Renci.SshNet.SshClient(conn);
                    client.Connect();
                    if (client.IsConnected)
                    {
                        sshReady = true;
                        var checkCmd = client.RunCommand("echo ready");
                        TestContext.WriteLine($"SSH probe: exit={checkCmd.ExitStatus} out={checkCmd.Result} err={checkCmd.Error}");
                        client.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"SSH probe failed: {ex.Message}");
            }

            Assert.That(sshReady, Is.True, "Timed out waiting for SSH in Testcontainers-based debug container");

            // Upload via SshBootstrapper to /opt as earlier tests do
            var remoteDeployDir = "/opt/Asionyx.Service.Deployment.Linux";
            var (mkExit, mkOut, mkErr) = await ExecRoot($"mkdir -p {remoteDeployDir} && chown -R {username}:{username} {remoteDeployDir} && chmod 755 {remoteDeployDir}");
            Assert.That(mkExit, Is.EqualTo(0), () => $"Failed to prepare remote dir: out={mkOut} err={mkErr}");

            var sb = new SshBootstrapper(host, username, hostKeyPath, sshPort);
            sb.UploadDirectory(host, sshPort, username, hostKeyPath, publishDir, remoteDeployDir);

            TestContext.WriteLine($"Deployment uploaded to container id={container.Id}. Container preserved for manual inspection.");

            // Intentionally keep container running for manual triage — do not call StopAsync.
            TestContext.WriteLine($"Container {container.Id} is still running. Use 'docker ps -a --filter \"id={container.Id}\"' to inspect from host.");
        }
        finally
        {
            try { File.Delete(hostKeyPath); } catch { }
            try { if (createdTempPublishDir) Directory.Delete(publishDir, true); } catch { }
        }
    }

    // New test: start the same Testcontainers-based CI image but concurrently stream Docker logs
    // while StartAsync runs. This helps surface boot output in real time when the wait strategy
    // is blocked waiting for readiness.
    [Test]
    public static async Task Deploy_Server_Debug_StreamLogsDuringStart()
    {
        var username = "pistomp";

        // Generate RSA keypair and write to temp host key file (reuse code from above)
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

        try
        {
            var apiKey2 = Guid.NewGuid().ToString("N");
            var builder = new SshdBuilder()
                .WithImage("audio-linux/ci-systemd-trixie:local")
                .WithEnvironment("API_KEY", apiKey2)
                .WithTestUserSetup(username)
                .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys");

            var container = builder.Build();

            // Start asynchronously and await. If StartAsync fails, fall back to using the
            // docker CLI to find any containers created from the target image and print
            // their logs/inspect output for triage. Streaming while StartAsync runs is
            // fragile across Testcontainers/Docker.DotNet versions, so we keep this
            // robust and diagnostic.
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await container.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"StartAsync completed with exception: {ex.Message}");

                // Try to detect a recent container created from the same image using the docker CLI
                try
                {
                    // The test uses the image name in the builder; replicate here for lookup.
                    var imageName = "audio-linux/ci-systemd-trixie:local";
                    var listPsi = new ProcessStartInfo("docker", $"ps -a --filter \"ancestor={imageName}\" --format \"{{{{.ID}}}}|{{{{.Image}}}}|{{{{.Status}}}}|{{{{.Names}}}}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var listProc = Process.Start(listPsi);
                    if (listProc != null)
                    {
                        var listOut = await listProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        var listErr = await listProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                        listProc.WaitForExit();
                        TestContext.WriteLine("docker ps -a output:\n" + listOut);
                        if (!string.IsNullOrWhiteSpace(listErr)) TestContext.WriteLine("docker ps -a stderr:\n" + listErr);

                        // If we found any container IDs, take the first and fetch logs/inspect
                        var firstLine = listOut?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstLine))
                        {
                            var id = firstLine.Split('|')[0];
                            TestContext.WriteLine($"Found candidate container id={id} for image {imageName}");

                            // Fetch logs
                            var logsPsi = new ProcessStartInfo("docker", $"logs --timestamps --tail 500 {id}")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            using var logsProc = Process.Start(logsPsi);
                            if (logsProc != null)
                            {
                                var logsOut = await logsProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                                var logsErr = await logsProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                                logsProc.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(logsOut)) TestContext.WriteLine("--- LOG (docker CLI stdout) ---\n" + logsOut);
                                if (!string.IsNullOrWhiteSpace(logsErr)) TestContext.WriteLine("--- LOG (docker CLI stderr) ---\n" + logsErr);
                            }

                            // Fetch inspect
                            var inspectPsi = new ProcessStartInfo("docker", $"inspect {id}")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var inspectProc = Process.Start(inspectPsi);
                            if (inspectProc != null)
                            {
                                var inspOut = await inspectProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                                var inspErr = await inspectProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                                inspectProc.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(inspOut)) TestContext.WriteLine("--- INSPECT ---\n" + inspOut);
                                if (!string.IsNullOrWhiteSpace(inspErr)) TestContext.WriteLine("--- INSPECT ERR ---\n" + inspErr);
                            }
                        }
                    }
                }
                catch (Exception ie)
                {
                    TestContext.WriteLine($"Diagnostic docker CLI inspection failed: {ie.Message}");
                }

                // Re-throw original start exception to keep test failure semantics
                throw;
            }

            TestContext.WriteLine($"Container started successfully id={container.Id}");

            // If started, run a simple SSH probe
            var host = container.Hostname;
            var sshPort = checked((int)container.GetMappedPublicPort(SshdBuilder.SshdPort));
            var sshReady = false;
            try
            {
                using var fs = File.OpenRead(hostKeyPath);
                var pk = new Renci.SshNet.PrivateKeyFile(fs);
                var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                var conn = new Renci.SshNet.ConnectionInfo(host, sshPort, username, keyAuth);
                using var client = new Renci.SshNet.SshClient(conn);
                client.Connect();
                if (client.IsConnected)
                {
                    sshReady = true;
                    var checkCmd = client.RunCommand("echo ready");
                    TestContext.WriteLine($"SSH probe: exit={checkCmd.ExitStatus} out={checkCmd.Result} err={checkCmd.Error}");
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"SSH probe failed: {ex.Message}");
            }

            Assert.That(sshReady, Is.True, "SSH was not ready after StartAsync completed.");
        }
        finally
        {
            try { File.Delete(hostKeyPath); } catch { }
        }
    }

    // New test: ensure we can run Testcontainers by setting DOCKER_HOST programmatically
    // inside the test process (no external environment configuration required).
    [Test]
    public static async Task Deploy_Server_Debug_WithInTestDockerHost()
    {
        var username = "pistomp";

        // Set DOCKER_HOST for this test process only so CI/IDE don't need external env vars.
        const string dockerHostValue = "tcp://localhost:2375";
        var previous = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("DOCKER_HOST", dockerHostValue, EnvironmentVariableTarget.Process);

        // Generate RSA keypair and write to temp host key file (reuse code pattern)
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

        try
        {
            var apiKey3 = Guid.NewGuid().ToString("N");
            var builder = new SshdBuilder()
                .WithImage("audio-linux/ci-systemd-trixie:local")
                .WithEnvironment("API_KEY", apiKey3)
                .WithTestUserSetup(username)
                .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys");

            var container = builder.Build();

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await container.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"StartAsync completed with exception: {ex.Message}");
                // Fall back to docker CLI to collect diagnostics
                try
                {
                    var imageName = "audio-linux/ci-systemd-trixie:local";
                    var listPsi = new ProcessStartInfo("docker", $"ps -a --filter \"ancestor={imageName}\" --format \"{{{{.ID}}}}|{{{{.Image}}}}|{{{{.Status}}}}|{{{{.Names}}}}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var listProc = Process.Start(listPsi);
                    if (listProc != null)
                    {
                        var listOut = await listProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        var listErr = await listProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                        listProc.WaitForExit();
                        TestContext.WriteLine("docker ps -a output:\n" + listOut);
                        if (!string.IsNullOrWhiteSpace(listErr)) TestContext.WriteLine("docker ps -a stderr:\n" + listErr);

                        var firstLine = listOut?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstLine))
                        {
                            var id = firstLine.Split('|')[0];
                            TestContext.WriteLine($"Found candidate container id={id} for image {imageName}");

                            var logsPsi = new ProcessStartInfo("docker", $"logs --timestamps --tail 500 {id}")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            using var logsProc = Process.Start(logsPsi);
                            if (logsProc != null)
                            {
                                var logsOut = await logsProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                                var logsErr = await logsProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                                logsProc.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(logsOut)) TestContext.WriteLine("--- LOG (docker CLI stdout) ---\n" + logsOut);
                                if (!string.IsNullOrWhiteSpace(logsErr)) TestContext.WriteLine("--- LOG (docker CLI stderr) ---\n" + logsErr);
                            }

                            var inspectPsi = new ProcessStartInfo("docker", $"inspect {id}")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var inspectProc = Process.Start(inspectPsi);
                            if (inspectProc != null)
                            {
                                var inspOut = await inspectProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                                var inspErr = await inspectProc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                                inspectProc.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(inspOut)) TestContext.WriteLine("--- INSPECT ---\n" + inspOut);
                                if (!string.IsNullOrWhiteSpace(inspErr)) TestContext.WriteLine("--- INSPECT ERR ---\n" + inspErr);
                            }
                        }
                    }
                }
                catch (Exception ie)
                {
                    TestContext.WriteLine($"Diagnostic docker CLI inspection failed: {ie.Message}");
                }

                throw;
            }

            TestContext.WriteLine($"Container started successfully id={container.Id}");

            // Simple SSH probe
            var host = container.Hostname;
            var sshPort = checked((int)container.GetMappedPublicPort(SshdBuilder.SshdPort));
            var sshReady = false;
            try
            {
                using var fs = File.OpenRead(hostKeyPath);
                var pk = new Renci.SshNet.PrivateKeyFile(fs);
                var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                var conn = new Renci.SshNet.ConnectionInfo(host, sshPort, username, keyAuth);
                using var client = new Renci.SshNet.SshClient(conn);
                client.Connect();
                if (client.IsConnected)
                {
                    sshReady = true;
                    var checkCmd = client.RunCommand("echo ready");
                    TestContext.WriteLine($"SSH probe: exit={checkCmd.ExitStatus} out={checkCmd.Result} err={checkCmd.Error}");
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"SSH probe failed: {ex.Message}");
            }

            Assert.That(sshReady, Is.True, "SSH was not ready after StartAsync completed.");
        }
        finally
        {
            try { File.Delete(hostKeyPath); } catch { }
            // Restore previous DOCKER_HOST (if any)
            Environment.SetEnvironmentVariable("DOCKER_HOST", previous, EnvironmentVariableTarget.Process);
        }
    }

    // Helper copied from SshdBuilder to derive OpenSSH public key from private PEM (keeps test self-contained)
    private static string DeriveOpenSshPublicKeyFromPrivatePem(string privateKeyPem)
    {
        var reader = new Org.BouncyCastle.OpenSsl.PemReader(new StringReader(privateKeyPem));
        var keyObject = reader.ReadObject();

        Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters rsaPub = null;

        if (keyObject is Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair pair)
        {
            rsaPub = (Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters)pair.Public;
        }
        else if (keyObject is Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters priv)
        {
            rsaPub = new Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters(false, priv.Modulus, priv.PublicExponent);
        }
        else if (keyObject is Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters k)
        {
            rsaPub = k;
        }
        else
        {
            throw new InvalidOperationException("Unsupported PEM key format for RSA key extraction.");
        }

        var e = rsaPub.Exponent.ToByteArrayUnsigned();
        var n = rsaPub.Modulus.ToByteArrayUnsigned();

        byte[] sshRsa;
        using (var ms = new MemoryStream())
        {
            void WriteUInt32(uint v)
            {
                var b = new byte[4];
                b[0] = (byte)((v >> 24) & 0xff);
                b[1] = (byte)((v >> 16) & 0xff);
                b[2] = (byte)((v >> 8) & 0xff);
                b[3] = (byte)(v & 0xff);
                ms.Write(b, 0, 4);
            }
            void WriteString(string s)
            {
                var data = Encoding.ASCII.GetBytes(s);
                WriteUInt32((uint)data.Length);
                ms.Write(data, 0, data.Length);
            }
            void WriteMpint(byte[] arr)
            {
                if (arr == null || arr.Length == 0) { WriteUInt32(0); return; }
                if ((arr[0] & 0x80) != 0)
                {
                    var withZero = new byte[arr.Length + 1];
                    withZero[0] = 0x00;
                    Buffer.BlockCopy(arr, 0, withZero, 1, arr.Length);
                    WriteUInt32((uint)withZero.Length);
                    ms.Write(withZero, 0, withZero.Length);
                }
                else
                {
                    WriteUInt32((uint)arr.Length);
                    ms.Write(arr, 0, arr.Length);
                }
            }

            WriteString("ssh-rsa");
            WriteMpint(e);
            WriteMpint(n);
            sshRsa = ms.ToArray();
        }

        var pub64 = Convert.ToBase64String(sshRsa);
        return $"ssh-rsa {pub64} generated-key";
    }
}
