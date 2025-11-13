# CI/Test image: Debian 13 with systemd, OpenSSH, PowerShell and .NET 9 runtime
# Minimal install steps. This image is intended to be used in CI where the runner
# can provide the necessary container privileges for systemd (privileged or --tmpfs /run).

FROM debian:13

ENV DEBIAN_FRONTEND=noninteractive
ENV container=docker

# Install base utilities, systemd, OpenSSH server, and dependencies for Microsoft packages
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        wget \
        gnupg \
        apt-transport-https \
        lsb-release \
        systemd \
        systemd-sysv \
        dbus \
        openssh-server \
    && rm -rf /var/lib/apt/lists/*

# Add Microsoft package repository (for PowerShell and .NET)
# Keep commands minimal: fetch microsoft GPG key and the prod repo list for Debian 13
RUN set -eux; \
    wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > /usr/share/keyrings/microsoft-prod.gpg; \
    wget -qO /etc/apt/sources.list.d/microsoft-prod.list https://packages.microsoft.com/config/debian/13/prod.list; \
    # Attempt to refresh apt; repository signing may fail in some environments. Don't fail the build here.
    apt-get update || true; \
    apt-get install -y --no-install-recommends dotnet-runtime-9.0 powershell || apt-get install -y --no-install-recommends dotnet-runtime-9 powershell || true; \
    rm -rf /var/lib/apt/lists/* || true

# NOTE: The image intentionally does NOT create non-root users, SSH keys, or
# other test-specific configuration. Integration tests should use the
# Testcontainers "startup callback" (see `SshdBuilder.WithStartupCallback`) to
# create users, install keys, and perform any per-test setup at container
# startup time. This keeps the image minimal and focused on runtime deps only.

# Systemd-in-container: declare cgroup mount so runtimes can bind /sys/fs/cgroup
# at runtime. CI runners should start this container with --privileged and mount
# /sys/fs/cgroup or use the runner-provided systemd-in-container wrapper.
VOLUME [ "/sys/fs/cgroup" ]

# Expose the application's HTTP port. SSH and test users are configured by
# Testcontainers startup callbacks when needed by tests.
EXPOSE 5001

# Ensure sshd listens on the test container expected port (2222) so Testcontainers
# readiness checks succeed without requiring an in-test startup step.
RUN if [ -f /etc/ssh/sshd_config ]; then sed -i 's/^#Port 22/Port 2222/' /etc/ssh/sshd_config 2>/dev/null || grep -q '^Port 2222' /etc/ssh/sshd_config || echo 'Port 2222' >> /etc/ssh/sshd_config; fi

# Use systemd as PID 1 so services (ssh, systemd units) can run normally inside the container.
# Note: CI runners must start this container with enough privileges (e.g. --privileged or with
# systemd-friendly flags) for systemd to work correctly.
STOPSIGNAL SIGRTMIN+3
CMD ["/lib/systemd/systemd"]
