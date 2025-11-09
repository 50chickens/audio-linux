using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;
using Testcontainers.Sshd;
using Asionyx.Tools.Deployment.Ssh;

namespace Asionyx.Tools.Deployment.Ssh.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class HelloWorldDockerTests
{
    [Test]
    public async Task HelloWorld_via_SshBootstrapper_On_SshdContainer()
    {
        // generate RSA keypair
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();

        string privatePem;
        using (var sw = new StringWriter())
        {
            var pw = new PemWriter(sw);
            pw.WriteObject(keyPair.Private);
            pw.Writer.Flush();
            privatePem = sw.ToString();
        }

        // write private key to a temp file for SshBootstrapper to use
        var tmpDir = Path.Combine(Path.GetTempPath(), "asionyx-ssh-test");
        Directory.CreateDirectory(tmpDir);
        var privateKeyPath = Path.Combine(tmpDir, $"id_rsa_{Guid.NewGuid():N}.pem");
        File.WriteAllText(privateKeyPath, privatePem, Encoding.ASCII);

        var username = "tcuser";

        await using var container = new SshdBuilder()
            .WithUsername(username)
            .WithPrivateKey(privatePem, containerPrivateKeyPath: $"/home/{username}/.ssh/id_rsa", containerPublicKeyPath: $"/home/{username}/.ssh/authorized_keys")
            .Build();

        await container.StartAsync(CancellationToken.None);

        try
        {
            var host = container.Hostname;
            var port = (int)container.GetMappedPublicPort(SshdBuilder.SshdPort);

            var sb = new SshBootstrapper(host, port, username, privateKeyPath);
            var result = sb.RunCommand("echo hello-asionyx");

            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Output?.Trim(), Is.EqualTo("hello-asionyx"));
        }
        finally
        {
            try { await container.DisposeAsync(); } catch { }
            try { File.Delete(privateKeyPath); } catch { }
        }
    }
}
