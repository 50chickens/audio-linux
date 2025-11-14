FROM debian:trixie-slim

ENV DEBIAN_FRONTEND=noninteractive

# Install minimal deps and tools (curl needed for the systemctl shim)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        wget \
        gnupg \
        lsb-release \
        apt-transport-https \
        sudo \
        procps \
        iproute2 \
        openssh-client \
        bash \
        libicu-dev \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 9 runtime via the official dotnet-install script
RUN set -eux; \
    wget -qO /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh; \
    bash /tmp/dotnet-install.sh --channel 9.0 --runtime dotnet --install-dir /usr/share/dotnet; \
    # Also install the ASP.NET Core shared framework so WebApplication apps can run
    bash /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /usr/share/dotnet; \
    ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet; \
    rm -f /tmp/dotnet-install.sh || true

# Create app folder
WORKDIR /app

# Copy published outputs (set by build script) into the image
COPY publish/Deployment ./deployment
COPY publish/Systemd ./systemd
COPY publish/TestTarget ./testtarget

# Systemctl shim: translate systemctl calls to systemd-emulator HTTP API
COPY tools/systemctl-shim.sh /usr/local/bin/systemctl
RUN chmod +x /usr/local/bin/systemctl

# Entrypoint wrapper: start the systemd emulator and then exec the deployment service
COPY tools/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 5001 5200

ENTRYPOINT ["/entrypoint.sh"]
