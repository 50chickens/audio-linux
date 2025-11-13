FROM debian:bookworm-slim

# Small systemd-in-container image based on https://github.com/xrowgmbh/docker-systemd
ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       systemd \
       systemd-sysv \
       dbus \
       ca-certificates \
       openssh-server \
       sudo \
       curl \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Ensure sshd uses the internal test port 2222 and allows publickey auth
RUN mkdir -p /var/run/sshd \
    && sed -i 's/#Port 22/Port 2222/' /etc/ssh/sshd_config || true \
    && sed -i 's/#PubkeyAuthentication yes/PubkeyAuthentication yes/' /etc/ssh/sshd_config || true \
    && sed -i 's/#AuthorizedKeysFile/AuthorizedKeysFile/' /etc/ssh/sshd_config || true \
    && sed -i 's/PermitRootLogin prohibit-password/PermitRootLogin no/' /etc/ssh/sshd_config || true \
    && sed -i 's/#PasswordAuthentication yes/PasswordAuthentication no/' /etc/ssh/sshd_config || true \
    && sed -i 's/#ChallengeResponseAuthentication no/ChallengeResponseAuthentication no/' /etc/ssh/sshd_config || true \
    && mkdir -p /root/.ssh

# Generate host keys if missing
RUN ssh-keygen -A || true

# A volume for cgroups - expected by systemd
VOLUME ["/sys/fs/cgroup"]

# Expose the internal SSH port used by tests
EXPOSE 2222

# Use systemd as PID 1
STOPSIGNAL SIGRTMIN+3
CMD ["/lib/systemd/systemd"]
