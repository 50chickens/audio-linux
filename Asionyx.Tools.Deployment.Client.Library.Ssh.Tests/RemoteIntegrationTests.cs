using NUnit.Framework;
using System;
using System.IO;

namespace Asionyx.Tools.Deployment.Client.Library.Ssh.Tests
{
    public class RemoteIntegrationTests
    {
        [Test]
        public void Remote_Ssh_PrivateKey_Integration_Test_If_EnvSet()
        {
            var host = Environment.GetEnvironmentVariable("SSH_INTEGRATION_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("SSH_INTEGRATION_HOST not set; skipping integration test.");
                return;
            }

            var user = Environment.GetEnvironmentVariable("SSH_INTEGRATION_USER") ?? "pistomp";
            var keyPath = Environment.GetEnvironmentVariable("SSH_INTEGRATION_KEY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
            var portStr = Environment.GetEnvironmentVariable("SSH_INTEGRATION_PORT");
            int port = 22;
            if (!string.IsNullOrEmpty(portStr) && !int.TryParse(portStr, out port)) port = 22;

            if (!File.Exists(keyPath))
            {
                Assert.Ignore($"Private key not found at {keyPath}; skipping integration test.");
                return;
            }

            // Use the locally referenced SSH.NET build via Renci.SshNet types
            try
            {
                using var fs = File.OpenRead(keyPath);
                var pkey = new Renci.SshNet.PrivateKeyFile(fs);
                var auth = new Renci.SshNet.PrivateKeyAuthenticationMethod(user, pkey);
                var conn = new Renci.SshNet.ConnectionInfo(host, port, user, auth);
                using var ssh = new Renci.SshNet.SshClient(conn);
                ssh.Connect();
                var cmd = ssh.RunCommand("echo integration-hello");
                ssh.Disconnect();

                Assert.That(cmd.ExitStatus == 0, "Remote command did not exit 0");
                Assert.That((cmd.Result ?? string.Empty).Contains("integration-hello"), "Remote command output did not contain expected text.");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Integration SSH attempt threw: {ex}");
            }
        }
    }
}
