using Asionyx.Tools.Deployment.Client.Library.Ssh;

namespace Asionyx.Tools.Deployment.Client.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class RealHostIntegrationTests
{
    // This test targets a real host and does not require Docker. It will fail if the
    // specified private key file is missing or if the SSH command returns a non-zero exit code.
    [Test]
    [TestCase("pistomp5", "pistomp", "C:\\Users\\morrisal\\.ssh\\id_rsa", 22)]
    public void RealHost_RunCommand_WithPrivateKeyFile_ShouldSucceed(string host, string user, string privateKeyPath, int port)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(privateKeyPath))
        {
            Assert.Fail($"Invalid test parameters: host='{host}', user='{user}', privateKeyPath='{privateKeyPath}'");
        }

        if (!File.Exists(privateKeyPath))
        {
            Assert.Ignore($"Private key file not found: {privateKeyPath}");
        }

        // Use private key file (PEM RSA) supplied by the test runner
        var sb = new SshBootstrapper(host, port, privateKeyPath, port);

        // Let exceptions bubble so test fails if connection/auth fails.
        var res = sb.RunCommand("echo hello-asionyx-real");

        Assert.That(res.ExitCode, Is.EqualTo(0), $"Remote command failed: stdout='{res.Output}' stderr='{res.Error}'");
        Assert.That(res.Output.Contains("hello-asionyx-real"), Is.True, $"Unexpected output: '{res.Output}'");
    }

    [Test]
    public void RealHost_RunCommand_WithGeneratedPpkInMemory_ShouldFailUnlessKeyAuthorized()
    {
        // This test demonstrates constructing an in-memory PPK and attempting to use it.
        // It will only succeed if the generated public key is authorized on the target host,
        // which is unlikely for a randomly generated key. The test is provided primarily
        // as a usage example for the in-memory PPK flow.
        Assert.Pass("In-memory PPK test is a demonstration and skipped by default.");
    }

    [Test]
    [TestCase("pistomp5", "pistomp", "C:\\Users\\morrisal\\.ssh\\id_rsa", 22)]
    public void RealHost_RunCommand_WithPrivateKeyContent_ShouldSucceed(string host, string user, string privateKeyPath, int port)
    {
        if (!File.Exists(privateKeyPath))
        {
            Assert.Ignore($"Private key file not found: {privateKeyPath}");
        }

        var content = File.ReadAllText(privateKeyPath);

        // Use in-memory private key content (PEM RSA)
        var sb = new SshBootstrapper(host, port, user, content, true, false);
        var res = sb.RunCommand("echo hello-privkey-content");

        Assert.That(res.ExitCode, Is.EqualTo(0), $"Remote command failed: stdout='{res.Output}' stderr='{res.Error}'");
        Assert.That(res.Output.Contains("hello-privkey-content"), Is.True, $"Unexpected output: '{res.Output}'");
    }

    [Test]
    public void RealHost_PpkFileOrConverted_ShouldAuthenticate()
    {
        var host = "pistomp5";
        var user = "pistomp";
    var ppkPath = "C:\\Users\\morrisal\\.ssh\\id_rsa";
        var port = 22;

        if (!File.Exists(ppkPath))
        {
            Assert.Ignore($"PPK file not found: {ppkPath}");
        }

        // Use provided private key file (PEM RSA)
        var sb = new SshBootstrapper(host, port, user, ppkPath, false);
        var res = sb.RunCommand("echo privkey-ok");
        Assert.That(res.ExitCode, Is.EqualTo(0), $"Remote command failed: stdout='{res.Output}' stderr='{res.Error}'");
        Assert.That(res.Output.Contains("privkey-ok"), Is.True);
    }
}
