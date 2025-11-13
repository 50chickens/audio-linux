FROM debian:trixie-slim

ENV DEBIAN_FRONTEND=noninteractive
ENV container=docker

# Install base runtime deps and systemd + OpenSSH
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        wget \
        gnupg \
        lsb-release \
        apt-transport-https \
    systemd \
    systemd-sysv \
    dbus \
    openssh-server \
    procps \
    iproute2 \
    libicu-dev \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 9 runtime via the official dotnet-install script to ensure availability
RUN set -eux; \
    wget -qO /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh; \
    bash /tmp/dotnet-install.sh --channel 9.0 --runtime dotnet --install-dir /usr/share/dotnet; \
    ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet; \
    rm -f /tmp/dotnet-install.sh || true

# Install latest PowerShell by downloading the release tarball and extracting it.
# We avoid the distro package path because the install script does not yet support Debian 13.
RUN set -eux; \
    asset_url=$(curl -s https://api.github.com/repos/PowerShell/PowerShell/releases/latest | grep 'browser_download_url' | grep 'linux-x64.tar.gz' | head -n1 | cut -d '"' -f4); \
    if [ -z "$asset_url" ]; then echo "Failed to find PowerShell release asset" >&2; exit 1; fi; \
    wget -qO /tmp/pw.tar.gz "$asset_url"; \
    mkdir -p /opt/microsoft/powershell; \
    tar -xzf /tmp/pw.tar.gz -C /opt/microsoft/powershell; \
    chmod +x /opt/microsoft/powershell/pwsh || true; \
    ln -sf /opt/microsoft/powershell/pwsh /usr/bin/pwsh; \
    rm -f /tmp/pw.tar.gz || true

# Ensure ssh runtime dir exists
RUN mkdir -p /run/sshd /var/run/sshd || true

# Configure sshd: listen on internal port 2222 and enable publickey auth.
# Do NOT provision users or keys here; test runtime callbacks will handle per-test setup.
RUN if [ -f /etc/ssh/sshd_config ]; then \
      sed -i 's/^#Port 22/Port 2222/' /etc/ssh/sshd_config 2>/dev/null || true; \
      sed -i 's/^#PubkeyAuthentication.*/PubkeyAuthentication yes/' /etc/ssh/sshd_config 2>/dev/null || true; \
      sed -i 's/^#PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config 2>/dev/null || true; \
      grep -q '^AuthorizedKeysFile' /etc/ssh/sshd_config || echo 'AuthorizedKeysFile %h/.ssh/authorized_keys' >> /etc/ssh/sshd_config; \
    fi

VOLUME ["/sys/fs/cgroup"]
STOPSIGNAL SIGRTMIN+3
EXPOSE 2222 5001

# Use systemd as PID 1
CMD ["/lib/systemd/systemd"]
