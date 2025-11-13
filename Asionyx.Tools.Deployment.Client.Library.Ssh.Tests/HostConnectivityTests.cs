namespace Asionyx.Tools.Deployment.Client.Library.Ssh.Tests;

[TestFixture]
[Category("Integration")]
public class HostConnectivityTests
{
    [Test]
    [TestCase("pistomp5", "pistomp")]
    public void RunHelloWorld_On_Host(string host, string username, int port = 22)
    {
        // Allow overriding via environment variables for CI/local runs
        var envKey = Environment.GetEnvironmentVariable("SSH_INTEGRATION_KEY");
        var keyPath = !string.IsNullOrEmpty(envKey)
            ? envKey
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
        {
            Assert.Fail("host SSH test failed: host and username are required.");
            return;
        }

        if (!File.Exists(keyPath))
        {
            Assert.Ignore($"Private key not found at '{keyPath}'; skipping live host connectivity test.");
            return;
        }

        // Log the client-side advertised algorithms (helpful to confirm local SSH.NET supports modern algorithms)
        try
        {
            var probe = new Renci.SshNet.ConnectionInfo(host, port, username, new Renci.SshNet.PasswordAuthenticationMethod(username, "x"));
            var kexNames = probe.KeyExchangeAlgorithms.Keys;
            var hostKeyNames = probe.HostKeyAlgorithms.Keys;
            TestContext.WriteLine("Local SSH.NET advertised KEX algorithms: " + string.Join(",", kexNames));
            TestContext.WriteLine("Local SSH.NET advertised HostKey algorithms: " + string.Join(",", hostKeyNames));
        }
        catch (Exception ex)
        {
            TestContext.WriteLine("Failed to probe local ConnectionInfo algorithm lists: " + ex.ToString());
        }

        // Attempt a real connection using the SshBootstrapper (which will surface rich diagnostics on failure)
    var sb = new SshBootstrapper(host, username, keyPath, port);
        try
        {
            var (exit, output, error) = sb.RunCommand("echo hello-asionyx-");
            Assert.That(exit, Is.EqualTo(0), $"Remote command failed: {error}");
            Assert.That(output?.Trim(), Is.EqualTo("hello-asionyx-"));
        }
        catch (Exception ex)
        {
            // Fail the test but include full exception diagnostics (stack trace and any augmented algorithm lists)
            Assert.Fail("SSH connectivity attempt threw:\n" + ex.ToString());
        }
    }
}
