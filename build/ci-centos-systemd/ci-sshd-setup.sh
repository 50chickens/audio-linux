#!/bin/bash
set -euo pipefail

# CI startup helper: create user, install PUBLIC_KEY into authorized_keys,
# ensure sshd is configured for key auth and running on port 2222.

USER_NAME="${USER_NAME:-pistomp}"
PUBLIC_KEY="${PUBLIC_KEY:-}"

echo "[ci-sshd-setup] running for user=$USER_NAME"

# Ensure user exists
if id "$USER_NAME" >/dev/null 2>&1; then
    echo "[ci-sshd-setup] user $USER_NAME exists"
else
    echo "[ci-sshd-setup] creating user $USER_NAME"
    useradd -m -s /bin/bash "$USER_NAME"
    echo "$USER_NAME ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/${USER_NAME}
    chmod 0440 /etc/sudoers.d/${USER_NAME}
fi

# Ensure ssh host keys exist
ssh-keygen -A || true

# Prepare home .ssh
HOME_DIR=$(eval echo "~$USER_NAME")
mkdir -p "$HOME_DIR/.ssh"
chown "$USER_NAME:$USER_NAME" "$HOME_DIR/.ssh"
chmod 700 "$HOME_DIR/.ssh"

if [ -n "$PUBLIC_KEY" ]; then
    echo "[ci-sshd-setup] writing PUBLIC_KEY to $HOME_DIR/.ssh/authorized_keys"
    echo "$PUBLIC_KEY" >> "$HOME_DIR/.ssh/authorized_keys"
    chown "$USER_NAME:$USER_NAME" "$HOME_DIR/.ssh/authorized_keys"
    chmod 600 "$HOME_DIR/.ssh/authorized_keys"
fi

# Ensure sshd config contains Port 2222 and PubkeyAuthentication yes
grep -q "^Port 2222$" /etc/ssh/sshd_config || echo "Port 2222" >> /etc/ssh/sshd_config
sed -i 's/^#\?PubkeyAuthentication.*/PubkeyAuthentication yes/' /etc/ssh/sshd_config || true

echo "[ci-sshd-setup] starting sshd"
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    # Enable and start sshd; if fails, fall back to starting sshd directly
    if systemctl enable sshd >/dev/null 2>&1; then
        systemctl start sshd || /usr/sbin/sshd -D &
    else
        /usr/sbin/sshd -D &
    fi
else
    /usr/sbin/sshd -D &
fi

echo "[ci-sshd-setup] complete"
