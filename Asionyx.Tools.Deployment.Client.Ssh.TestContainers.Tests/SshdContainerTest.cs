using System.Text;
using Testcontainers.Sshd;
using System.Runtime.InteropServices;
using NUnit.Framework;
namespace DotNet.Testcontainers.Tests.Integration.Sshd;
[TestFixture]
[Category("RequiresHost")]
public class SshdContainerTest
{
    [SetUp]
    public void SkipIfWindowsHost()
    {
        RequiresHostHelper.EnsureHostOrIgnore();
    }

    [Test]
    public static async Task Sshd_WithPrivateKeyFromEnv_AllowsKeyAuth()
    {

        // Generate RSA keypair
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

        var username = "tcuser";

        // Ensure docker host is set for the test process so CI/IDE don't need external config
        var prevDh = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://localhost:2375", EnvironmentVariableTarget.Process);

        await using var container = new SshdBuilder()
            .WithImage("audio-linux/ci-systemd-trixie:local")
            // Bind host cgroup to enable systemd inside container when possible
            .WithBindMount("/sys/fs/cgroup", "/sys/fs/cgroup")
            .WithUsername(username)
            .WithPrivateKey(privatePem, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys")
            .Build();

    try
    {
        await container.StartAsync(CancellationToken.None);
    }
    finally
    {
        Environment.SetEnvironmentVariable("DOCKER_HOST", prevDh, EnvironmentVariableTarget.Process);
    }
        var host = container.Hostname;
    var sshPortRaw = container.GetMappedPublicPort(SshdBuilder.SshdPort);
    int sshPort = Convert.ToInt32(sshPortRaw);

            using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(privatePem)))
            {
                var pk = new Renci.SshNet.PrivateKeyFile(ms);
                using var client = new Renci.SshNet.SshClient(host, (int)sshPort, username, pk);
                client.Connect();
                Assert.That(client.IsConnected, Is.True);

                var cmd = client.RunCommand("whoami");
                Assert.That(cmd.ExitStatus, Is.EqualTo(0));
                Assert.That(cmd.Result.Trim(), Is.EqualTo(username));

                client.Disconnect();
            }

    }

    [Test]
    public static async Task Sshd_Scp_UploadFileAndVerifyContents()
    {

        // Generate RSA keypair
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

        var username = "tcuser";

        await using var container = new SshdBuilder()
            .WithUsername(username)
            .WithPrivateKey(privatePem, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys")
            .Build();

    await container.StartAsync(CancellationToken.None);

        var host = container.Hostname;
    var sshPortRaw = container.GetMappedPublicPort(SshdBuilder.SshdPort);
    int sshPort = Convert.ToInt32(sshPortRaw);

            // Upload a small file using SCP
            var content = "hello-scp";
            using (var msKey = new MemoryStream(Encoding.ASCII.GetBytes(privatePem)))
            {
                var pk = new Renci.SshNet.PrivateKeyFile(msKey);
                var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                var conn = new Renci.SshNet.ConnectionInfo(host, (int)sshPort, username, keyAuth);
                using var scp = new Renci.SshNet.ScpClient(conn);
                scp.Connect();
                Assert.That(scp.IsConnected, Is.True);

                using (var contentStream = new MemoryStream(Encoding.ASCII.GetBytes(content)))
                {
                    var targetPath = $"/tmp/{username}_received.txt";
                    scp.Upload(contentStream, targetPath);
                }

                scp.Disconnect();
            }

            // Verify the uploaded file contents using an SSH command
            using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(privatePem)))
            {
                var pk = new Renci.SshNet.PrivateKeyFile(ms);
                var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
                var conn = new Renci.SshNet.ConnectionInfo(host, (int)sshPort, username, keyAuth);
                using var client = new Renci.SshNet.SshClient(conn);
                client.Connect();
                Assert.That(client.IsConnected, Is.True);

                var cmd = client.RunCommand($"cat /tmp/{username}_received.txt");
                Assert.That(cmd.ExitStatus, Is.EqualTo(0));
                Assert.That(cmd.Result.Trim(), Is.EqualTo(content));

                client.Disconnect();
            }

    }

    [Test]
    public static async Task Sshd_PrivateKeyFileCopied_AllowsKeyAuth()
    {
        // Generate RSA keypair
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

        // Write private key to a temp host file that will be copied into the container
        var hostKeyPath = Path.Combine(Path.GetTempPath(), $"testcontainers_ssh_key_{Guid.NewGuid():N}");
        File.WriteAllText(hostKeyPath, privatePem, Encoding.ASCII);

        try
        {
            var username = "tcuser";

            await using var container = new SshdBuilder()
                .WithUsername(username)
                .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys")
                .Build();

            await container.StartAsync(CancellationToken.None);

            var host = container.Hostname;
            var sshPortRaw = container.GetMappedPublicPort(SshdBuilder.SshdPort);
            int sshPort = Convert.ToInt32(sshPortRaw);

            // Use the host key file as the client key as well
            using var fs = File.OpenRead(hostKeyPath);
            var pk = new Renci.SshNet.PrivateKeyFile(fs);
            var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(username, pk);
            var conn = new Renci.SshNet.ConnectionInfo(host, (int)sshPort, username, keyAuth);
            using var client = new Renci.SshNet.SshClient(conn);
            client.Connect();
            Assert.That(client.IsConnected, Is.True);

            var cmd = client.RunCommand("whoami");
            Assert.That(cmd.ExitStatus, Is.EqualTo(0));
            Assert.That(cmd.Result.Trim(), Is.EqualTo(username));

            client.Disconnect();
        }
        finally
        {
            try { File.Delete(hostKeyPath); } catch { }
        }
    }
}
