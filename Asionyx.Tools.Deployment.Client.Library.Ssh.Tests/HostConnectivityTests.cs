namespace Asionyx.Tools.Deployment.Client.Library.Ssh.Tests;

[TestFixture]
[Category("Integration")]
public class HostConnectivityTests
{
    [Test]
    [TestCase("pistomp5", "pistomp")]
    public void RunHelloWorld_On_Host(string host, string username, int port = 22)
    {
        
        var userNameFromEnvironment = Environment.GetEnvironmentVariable("USERNAME");
        var privateKeyFileName = $"C:\\Users\\{userNameFromEnvironment}\\.ssh\\id_rsa"; //get the user name from the USERNAME environment variable

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(privateKeyFileName))
        {
            Assert.Fail("host SSH test failed: provide ssh-host, ssh-port, ssh-user and ssh-key test parameters to run.");
            return;
        }

        if (!File.Exists(privateKeyFileName))
        {
            Assert.Fail($"host SSH test failed: key file '{privateKeyFileName}' does not exist.");
            return;
        }

    var sb = new SshBootstrapper(host, username, privateKeyFileName, port);
    var (exit, output, error) = sb.RunCommand("echo hello-asionyx-");

        Assert.That(exit, Is.EqualTo(0), $"Remote command failed: {error}");
        Assert.That(output?.Trim(), Is.EqualTo("hello-asionyx-"));
    }
}
