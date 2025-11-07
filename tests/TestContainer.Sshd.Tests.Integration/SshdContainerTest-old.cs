using System.Text;
using Assert = NUnit.Framework.Assert;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Testcontainers.Sshd;
using Renci.SshNet;
public class SshdContainerTest
{
    [Test]
    [TestCase("tcuser")]
    public async Task Sshd_WithPrivateKeyFromEnv_AllowsKeyAuth(string username)
    {

        // Generate RSA keypair
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();

        string privatePem;
        using (var sw = new StringWriter())
        {
            var pw = new PemWriter(sw);
            pw.WriteObject(keyPair.Private);
            pw.Writer.Flush();
            privatePem = sw.ToString();
        }

        string containerPrivateKeyPath = $"/home/{username}/.ssh/id_rsa";
        string containerPublicKeyPath = $"/home/{username}/.ssh/authorized_keys";
        await using var container = new SshdBuilder()
            .WithUsername(username)
            .WithPrivateKey(privatePem, containerPrivateKeyPath, containerPublicKeyPath)
            .Build();

        await container.StartAsync();
        var host = container.Hostname;
        var sshPort = container.GetMappedPublicPort(SshdBuilder.SshdPort);

            using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(privatePem)))
            {
                var pk = new PrivateKeyFile(ms);
                using var client = new SshClient(host, (int)sshPort, username, pk);

                client.Connect();
                Assert.That(client.IsConnected, Is.True);

                var cmd = client.RunCommand("whoami");
                Assert.That(cmd.ExitStatus, Is.EqualTo(0));
                Assert.That(cmd.Result.Trim(), Is.EqualTo(username));

                client.Disconnect();
            }

    }

    [Test]
    [TestCase("tcuser")]
    public async Task Sshd_Scp_UploadFileAndVerifyContents(string username)
    {

        // Generate RSA keypair
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();

        string privatePem;
        using (var sw = new StringWriter())
        {
            var pw = new PemWriter(sw);
            pw.WriteObject(keyPair.Private);
            pw.Writer.Flush();
            privatePem = sw.ToString();
        }

        string containerPublicKeyPath = $"/home/{username}/.ssh/authorized_keys";
        string containerPrivateKeyPath = $"/home/{username}/.ssh/id_rsa";
        await using var container = new SshdBuilder()
            .WithUsername(username)
            .WithPrivateKey(privatePem, containerPrivateKeyPath, containerPublicKeyPath)
            .Build();

        await container.StartAsync();

        var host = container.Hostname;
    var sshPort = container.GetMappedPublicPort(SshdBuilder.SshdPort);

            // Upload a small file using SCP
            var content = "hello-scp";
            using (var msKey = new MemoryStream(Encoding.ASCII.GetBytes(privatePem)))
            {
                var pk = new PrivateKeyFile(msKey);
                var keyAuth = new PrivateKeyAuthenticationMethod(username, pk);
                var conn = new ConnectionInfo(host, (int)sshPort, username, keyAuth);
                using var scp = new ScpClient(conn);
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
                var pk = new PrivateKeyFile(ms);
                var keyAuth = new PrivateKeyAuthenticationMethod(username, pk);
                var conn = new ConnectionInfo(host, (int)sshPort, username, keyAuth);
                using var client = new SshClient(conn);
                client.Connect();
                Assert.That(client.IsConnected, Is.True);

                var cmd = client.RunCommand($"cat /tmp/{username}_received.txt");
                Assert.That(cmd.ExitStatus, Is.EqualTo(0));
                Assert.That(cmd.Result.Trim(), Is.EqualTo(content));

                client.Disconnect();
            }

    }

    [Test]
    [TestCase("tcuser")]
    public async Task Sshd_PrivateKeyFileCopied_AllowsKeyAuth(string username)
    {
        // Generate RSA keypair
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();

        string privatePem;
        using (var sw = new StringWriter())
        {
            var pw = new PemWriter(sw);
            pw.WriteObject(keyPair.Private);
            pw.Writer.Flush();
            privatePem = sw.ToString();
        }

        // Write private key to a temp host file that will be copied into the container
        var hostKeyPath = Path.Combine(Path.GetTempPath(), $"testcontainers_ssh_key_{Guid.NewGuid():N}");
        File.WriteAllText(hostKeyPath, privatePem, Encoding.ASCII);

        try
        {
         
            string containerPublicKeyPath = $"/home/{username}/.ssh/authorized_keys";
            string containerPrivateKeyPath = $"/home/{username}/.ssh/id_rsa";
            await using var container = new SshdBuilder()
                .WithUsername(username)
                .WithPrivateKeyFileCopied(hostKeyPath, containerPrivateKeyPath, containerPublicKeyPath)
                .Build();

            await container.StartAsync();

            var host = container.Hostname;
            var sshPort = container.GetMappedPublicPort(SshdBuilder.SshdPort);

            // Use the host key file as the client key as well
            using var fs = File.OpenRead(hostKeyPath);
            var pk = new PrivateKeyFile(fs);
            var keyAuth = new PrivateKeyAuthenticationMethod(username, pk);
            var conn = new ConnectionInfo(host, (int)sshPort, username, keyAuth);
            using var client = new SshClient(conn);

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
