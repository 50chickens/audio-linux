#!/bin/bash
set -e
echo "Disabling IPv6 permanently..."
sudo touch /etc/sysctl.conf
sudo sed -i '/^net.ipv6.conf.all.disable_ipv6/d' /etc/sysctl.conf
sudo sed -i '/^net.ipv6.conf.default.disable_ipv6/d' /etc/sysctl.conf
echo "net.ipv6.conf.all.disable_ipv6 = 1" | sudo tee -a /etc/sysctl.conf
echo "net.ipv6.conf.default.disable_ipv6 = 1" | sudo tee -a /etc/sysctl.conf
sudo sysctl -p
echo "IPv6 disabled. Reboot required for full effect."
echo "Rebooting in 5 seconds..."
for i in {5..1}; do
    echo "$i..."
    sleep 1
done
sudo reboot