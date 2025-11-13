using System;
using System.Threading;
using System.Threading.Tasks;

namespace Testcontainers.Sshd;

/// <summary>
/// Reusable startup callbacks for Sshd containers used by integration tests.
/// Keep callbacks idempotent and tolerant — they may run on images that already
/// have the packages or users installed.
/// </summary>
public static class SshdStartupHelpers
{
    /// <summary>
    /// Configure the container so it contains a non-root test user and an SSH server.
    /// The callback will:
    /// - create the user if missing
    /// - install openssh-server if missing (apt-based)
    /// - create the user's .ssh directory and populate authorized_keys from $PUBLIC_KEY
    /// - create a passwordless sudoers entry
    /// - attempt to start sshd so the container accepts SSH connections
    ///
    /// This is deliberately permissive: it attempts each step but does not throw on
    /// failure so tests running against different base images (including linuxserver
    /// or the minimal CI image) can reuse the same callback.
    /// </summary>
    public static SshdBuilder WithTestUserSetup(this SshdBuilder builder, string username = "pistomp", bool installSshd = true)
    {
        _ = builder ?? throw new ArgumentNullException(nameof(builder));

        return builder.WithStartupCallback(async (container, ct) =>
        {
            // create user if missing
            try
            {
                await container.ExecAsync(new[] { "/bin/sh", "-c", $"id -u {username} || useradd -m -s /bin/bash {username}" }, ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }

            // create sudoers entry
            try
            {
                await container.ExecAsync(new[] { "/bin/sh", "-c", $"echo \"{username} ALL=(ALL) NOPASSWD:ALL\" > /etc/sudoers.d/{username} || true && chmod 440 /etc/sudoers.d/{username}" }, ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }

            if (installSshd)
            {
                // install openssh-server if the image is apt-based. Be tolerant if apt is not present.
                try
                {
                    await container.ExecAsync(new[] { "/bin/sh", "-c", "apt-get update || true && apt-get install -y --no-install-recommends openssh-server || true" }, ct).ConfigureAwait(false);
                }
                catch { /* ignore */ }

                // ensure runtime dir exists
                try
                {
                    await container.ExecAsync(new[] { "/bin/sh", "-c", "mkdir -p /var/run/sshd || true" }, ct).ConfigureAwait(false);
                }
                catch { /* ignore */ }

                // Ensure sshd listens on port 2222 (Testcontainers Sshd builder expects internal 2222)
                try
                {
                    var fixPortCmd = "(grep -q '^Port 2222' /etc/ssh/sshd_config >/dev/null 2>&1) || (sed -i 's/^#Port 22/Port 2222/' /etc/ssh/sshd_config 2>/dev/null || echo 'Port 2222' >> /etc/ssh/sshd_config)";
                    await container.ExecAsync(new[] { "/bin/sh", "-c", fixPortCmd }, ct).ConfigureAwait(false);
                }
                catch { /* ignore */ }

                // Try to start sshd via any available init mechanism
                try
                {
                    await container.ExecAsync(new[] { "/bin/sh", "-c", "(service ssh status >/dev/null 2>&1 && service ssh restart) || (systemctl enable --now ssh || /etc/init.d/ssh start) || (sshd || /usr/sbin/sshd) || true" }, ct).ConfigureAwait(false);
                }
                catch { /* ignore */ }
            }

            // populate authorized_keys from PUBLIC_KEY environment variable if present
            try
            {
                var cmd = $"mkdir -p /home/{username}/.ssh && (if [ -n \"$PUBLIC_KEY\" ]; then echo \"$PUBLIC_KEY\" > /home/{username}/.ssh/authorized_keys; fi) || true && chmod 600 /home/{username}/.ssh/authorized_keys || true && chown -R {username}:{username} /home/{username}/.ssh || true";
                await container.ExecAsync(new[] { "/bin/sh", "-c", cmd }, ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }

            // Wait for sshd to be listening on the expected internal port (2222).
            // Use bash /dev/tcp probing since some images may not have ss/netstat.
            try
            {
                var waitCmd = "/bin/bash -c 'for i in {1..30}; do (echo >/dev/tcp/127.0.0.1/2222) && exit 0 || sleep 1; done; exit 0'";
                await container.ExecAsync(new[] { "/bin/sh", "-c", waitCmd }, ct).ConfigureAwait(false);
            }
            catch { /* ignore - readiness will still be checked by WaitStrategy */ }
        });
    }
}
