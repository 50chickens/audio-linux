using NUnit.Framework;
using Renci.SshNet;
using System.Linq;

namespace Asionyx.Tools.Deployment.Client.Library.Ssh.Tests
{
    public class ConnectionAlgorithmsTests
    {
        [Test]
        public void ConnectionInfo_Should_Expose_Modern_Kex_And_HostKey()
        {
            // Create a ConnectionInfo with a dummy PasswordAuthentication (no network call)
            var conn = new ConnectionInfo("dummy", 22, "user", new PasswordAuthenticationMethod("user", "pass"));

            // Check KEX algorithms contain curve25519
            var kex = conn.KeyExchangeAlgorithms.Keys.Select(k => k.ToLowerInvariant()).ToList();
            Assert.That(kex.Contains("curve25519-sha256") || kex.Contains("curve25519-sha256@libssh.org"), "ConnectionInfo does not contain curve25519 KEX algorithm.");

            // Check host key algorithms include ssh-ed25519 or rsa-sha2 variants
            var hostkeys = conn.HostKeyAlgorithms.Keys.Select(k => k.ToLowerInvariant()).ToList();
            Assert.That(hostkeys.Contains("ssh-ed25519") || hostkeys.Any(h => h.StartsWith("rsa-sha2-")) || hostkeys.Contains("ssh-rsa"), "ConnectionInfo does not contain ed25519 or rsa-sha2 host key algorithms.");
        }
    }
}
