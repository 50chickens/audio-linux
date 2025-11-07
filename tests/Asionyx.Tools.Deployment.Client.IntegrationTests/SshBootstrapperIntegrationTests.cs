using NUnit.Framework;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;
using System.IO;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;
using Asionyx.Tools.Deployment.Ssh;

namespace Asionyx.Tools.Deployment.Client.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class SshBootstrapperIntegrationTests
{
    // We'll create and configure the linuxserver/openssh-server container per-test so
    // the test can generate an ephemeral keypair and inject the public key via
    // the PUBLIC_KEY environment variable and USER_NAME environment variable.

    [Test]
    public async Task UploadFile_and_verify_exists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ssh-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(tmp);
        var privateKeyPath = Path.Combine(tmp, "id_rsa.pem");

        // generate keypair
        RsaKeyPairGenerator gen = new RsaKeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 2048));
        var pair = gen.GenerateKeyPair();

        // write private key PEM
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(pair.Private);
            pemWriter.Writer.Flush();
            File.WriteAllText(privateKeyPath, sw.ToString());
        }

        // build OpenSSH public key string
        var rsaPub = (RsaKeyParameters)pair.Public;
        byte[] e = rsaPub.Exponent.ToByteArrayUnsigned();
        byte[] n = rsaPub.Modulus.ToByteArrayUnsigned();
        byte[] sshRsa = BuildSshRsaPublicKey(e, n);
        var pub64 = System.Convert.ToBase64String(sshRsa);
        var pubKey = $"ssh-rsa {pub64} generated-key";
        // Start the container with the public key injected via environment variable.
        var sshUser = "testuser";
        var container = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("linuxserver/openssh-server:latest")
            .WithCleanUp(true)
            // linuxserver/openssh-server listens on 2222 inside the container
            .WithPortBinding(2222, true)
            .WithEnvironment("PUID", "1000")
            .WithEnvironment("PGID", "1000")
            .WithEnvironment("TZ", "Etc/UTC")
            .WithEnvironment("USER_NAME", sshUser)
            .WithEnvironment("PUBLIC_KEY", pubKey)
            .WithEnvironment("PASSWORD_ACCESS", "false")
            .WithEnvironment("SUDO_ACCESS", "false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(2222))
            .Build();

        await container.StartAsync();
        try
        {
            var host = container.Hostname;
            var port = container.GetMappedPublicPort(2222);

            var file = Path.Combine(tmp, "hello.txt");
            File.WriteAllText(file, "hello world");
            var remotePath = $"/tmp/{Path.GetFileName(file)}";

            var sb = new SshBootstrapper();
            // connect as testuser using key
            sb.UploadFile(host, port, sshUser, privateKeyPath, file, remotePath);

            var result = sb.RunCommand(host, port, sshUser, privateKeyPath, $"test -f {remotePath} && echo found || echo missing");
            Assert.AreEqual(0, result.ExitCode);
            Assert.IsTrue(result.Output.Trim().Contains("found"));
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static byte[] BuildSshRsaPublicKey(byte[] e, byte[] n)
    {
        using var ms = new MemoryStream();
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
            var data = System.Text.Encoding.ASCII.GetBytes(s);
            WriteUInt32((uint)data.Length);
            ms.Write(data, 0, data.Length);
        }
        void WriteMpint(byte[] arr)
        {
            if (arr.Length == 0) { WriteUInt32(0); return; }
            if ((arr[0] & 0x80) != 0)
            {
                // prefix 0
                var withZero = new byte[arr.Length + 1];
                withZero[0] = 0x00;
                System.Buffer.BlockCopy(arr, 0, withZero, 1, arr.Length);
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
        return ms.ToArray();
    }
}
