FROM centos/systemd:latest

ENV container docker
ENV DEBIAN_FRONTEND=noninteractive

# Install openssh-server and utilities
RUN yum -y install openssh-server openssh-clients passwd sudo shadow-utils && \
    yum clean all && rm -rf /var/cache/yum

# Create a default non-root test user 'pistomp' (can be overridden at runtime via USER_NAME if desired)
RUN useradd -m -s /bin/bash pistomp || true && \
    echo "pistomp ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/pistomp && chmod 0440 /etc/sudoers.d/pistomp

# Ensure ssh listens on internal port 2222
RUN sed -i 's/#Port 22/Port 2222/' /etc/ssh/sshd_config || true
RUN grep -q "^Port 2222$" /etc/ssh/sshd_config || echo "Port 2222" >> /etc/ssh/sshd_config
RUN sed -i 's/#PubkeyAuthentication yes/PubkeyAuthentication yes/' /etc/ssh/sshd_config || true

# Add startup helper and systemd unit to provision user and authorized_keys from PUBLIC_KEY
COPY build/ci-centos-systemd/ci-sshd-setup.sh /usr/local/bin/ci-sshd-setup.sh
RUN chmod +x /usr/local/bin/ci-sshd-setup.sh

COPY build/ci-centos-systemd/ci-sshd-setup.service /etc/systemd/system/ci-sshd-setup.service

# Enable the service by creating the symlink (avoid calling systemctl during build)
RUN mkdir -p /etc/systemd/system/multi-user.target.wants && \
    ln -sf /etc/systemd/system/ci-sshd-setup.service /etc/systemd/system/multi-user.target.wants/ci-sshd-setup.service || true

# Generate host keys
RUN ssh-keygen -A || true

EXPOSE 2222

VOLUME ["/sys/fs/cgroup"]

CMD ["/sbin/init"]
