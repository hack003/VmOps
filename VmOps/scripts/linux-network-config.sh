#!/bin/bash
set +e

NEW_IP="${IP}"
NEW_MASK="${MASK}"
NEW_GW="${GW}"
NEW_DNS="${DNS}"
NEW_HOST="${HOSTNAME}"

echo "========== Guest Operations Network Config =========="
echo "IP=$NEW_IP  Mask=$NEW_MASK  GW=$NEW_GW  DNS=$NEW_DNS  Host=$NEW_HOST"

if ! pgrep -f vmtoolsd > /dev/null 2>&1 && ! pgrep -f vmware-toolbox > /dev/null 2>&1; then
    echo "WARNING: VMware Tools process not detected"
fi

DEFAULT_IFACE=$(ip -br link 2>/dev/null | grep -v '^lo' | grep -w UP | awk '{print $1}' | head -1)
if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip -br link 2>/dev/null | grep -v '^lo' | grep -v DOWN | awk '{print $1}' | head -1)
fi
if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip -br link 2>/dev/null | grep -v '^lo\|LOOPBACK' | awk '{print $1}' | head -1)
fi
if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip route 2>/dev/null | grep default | awk '{print $5}' | head -1)
fi
echo "Interface: $DEFAULT_IFACE"

if [ -z "$DEFAULT_IFACE" ]; then
    echo "ERROR: No usable interface detected"
    exit 1
fi

if [ -f /etc/os-release ]; then
    . /etc/os-release
    echo "Distro: $PRETTY_NAME (${ID:-unknown})"
fi

hostnamectl set-hostname "$NEW_HOST" 2>/dev/null || hostname "$NEW_HOST"
echo "$NEW_HOST" > /etc/hostname 2>/dev/null || true
sed -i "s/^127\.0\.0\.1.*/127.0.0.1 localhost $NEW_HOST/" /etc/hosts 2>/dev/null || true

echo "nameserver $NEW_DNS" > /etc/resolv.conf
if [ -d /etc/resolvconf/resolv.conf.d/head ]; then
    echo "nameserver $NEW_DNS" > /etc/resolvconf/resolv.conf.d/head
    resolvconf -u 2>/dev/null || true
fi
if [ -d /etc/NetworkManager ]; then
    CONN=$(nmcli -t -f NAME,TYPE con show --active 2>/dev/null | grep ethernet | head -1 | cut -d: -f1)
    [ -n "$CONN" ] && nmcli con mod "$CONN" ipv4.dns "$NEW_DNS" 2>/dev/null
fi

APPLIED=0

if command -v nmcli > /dev/null 2>&1; then
    echo "→ NetworkManager (nmcli) detected, using nmcli path"
    echo "  Removing all stale connections..."
    nmcli -t -f NAME con show 2>/dev/null | while read -r cn; do
        [ -n "$cn" ] && [ "$cn" != "lo" ] && nmcli con delete "$cn" 2>/dev/null || true
    done
    sleep 1
    CONN='vmt-deploy'
    echo "  Creating connection '$CONN' on $DEFAULT_IFACE..."
    if nmcli con add type ethernet con-name "$CONN" ifname "$DEFAULT_IFACE" \
        ipv4.method manual \
        ipv4.addresses "${NEW_IP}/${NEW_MASK}" \
        ipv4.gateway  "${NEW_GW}" \
        ipv4.dns      "${NEW_DNS}" \
        ipv6.method ignore 2>&1; then
        if nmcli con up "$CONN" 2>&1; then
            sleep 2
            ACTUAL_IP=$(nmcli -g ipv4.addresses con show "$CONN" 2>/dev/null | cut -d/ -f1 | head -1)
            if [ -n "$ACTUAL_IP" ]; then
                echo "✓ nmcli succeeded, IP=$ACTUAL_IP"
                APPLIED=1
            else
                echo "WARNING: nmcli con up OK but IP empty, falling back to ip"
            fi
        else
            echo "WARNING: nmcli con up failed, falling back to ip"
        fi
    else
        echo "WARNING: nmcli con add failed, falling back to ip"
    fi

elif command -v netplan > /dev/null 2>&1 && ls /etc/netplan/*.yaml > /dev/null 2>&1; then
    echo "→ netplan detected (/etc/netplan/*.yaml), using netplan path"
    rm -f /etc/netplan/*.yaml 2>/dev/null || true
    NETPLAN_FILE="/etc/netplan/99-vmware-deploy.yaml"
    cat > "$NETPLAN_FILE" <<EOF
network:
  version: 2
  ethernets:
    ${DEFAULT_IFACE}:
      dhcp4: no
      addresses: [${NEW_IP}/${NEW_MASK}]
      gateway4: ${NEW_GW}
      nameservers:
        addresses: [${NEW_DNS}]
EOF
    netplan apply 2>/dev/null
    if [ $? -eq 0 ]; then
        sleep 2
        ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
        if [ -n "$ACTUAL_IP" ]; then
            echo "✓ netplan apply succeeded, IP=$ACTUAL_IP"
            APPLIED=1
        else
            echo "WARNING: netplan apply succeeded but IP not bound, falling back to ip"
        fi
    else
        echo "WARNING: netplan apply failed, falling back to ip"
    fi

elif ls /etc/sysconfig/network-scripts/ifcfg-* > /dev/null 2>&1; then
    echo "→ ifcfg detected (/etc/sysconfig/network-scripts/), using ifcfg path"
    IFCFG_FILE="/etc/sysconfig/network-scripts/ifcfg-${DEFAULT_IFACE}"
    cat > "$IFCFG_FILE" <<EOF
TYPE=Ethernet
BOOTPROTO=none
NAME="${DEFAULT_IFACE}"
DEVICE="${DEFAULT_IFACE}"
ONBOOT=yes
IPADDR=${NEW_IP}
PREFIX=${NEW_MASK}
GATEWAY=${NEW_GW}
DNS1=$(echo "$NEW_DNS" | awk '{print $1}')
DNS2=$(echo "$NEW_DNS" | awk '{print $2}')
EOF
    ifdown "$DEFAULT_IFACE" 2>/dev/null
    ifup "$DEFAULT_IFACE" 2>/dev/null
    if [ $? -eq 0 ]; then
        sleep 2
        ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
        if [ -n "$ACTUAL_IP" ]; then
            echo "✓ ifup succeeded, IP=$ACTUAL_IP"
            APPLIED=1
        else
            echo "WARNING: ifup succeeded but IP not bound, falling back to ip"
        fi
    else
        systemctl restart network 2>/dev/null || systemctl restart NetworkManager 2>/dev/null || true
        echo "WARNING: ifup failed, tried systemctl restart, falling back to ip"
    fi

elif [ -f /etc/network/interfaces ] || command -v ifup > /dev/null 2>&1; then
    echo "→ /etc/network/interfaces detected, using interfaces path"
    cat > /etc/network/interfaces <<EOF
auto lo
iface lo inet loopback

auto ${DEFAULT_IFACE}
iface ${DEFAULT_IFACE} inet static
    address ${NEW_IP}/${NEW_MASK}
    gateway ${NEW_GW}
EOF
    ifdown "$DEFAULT_IFACE" 2>/dev/null
    ifup "$DEFAULT_IFACE" 2>/dev/null
    if [ $? -eq 0 ]; then
        sleep 2
        ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
        if [ -n "$ACTUAL_IP" ]; then
            echo "✓ ifup succeeded, IP=$ACTUAL_IP"
            APPLIED=1
        else
            echo "WARNING: ifup succeeded but IP not bound, falling back to ip"
        fi
    else
        echo "WARNING: ifup failed, falling back to ip"
    fi
fi

if [ "$APPLIED" -eq 0 ]; then
    echo "→ Config tool did not apply, using ip command fallback"
    echo "  ip addr flush dev $DEFAULT_IFACE"
    ip addr flush dev "$DEFAULT_IFACE" 2>&1 || true
    echo "  ip addr add ${NEW_IP}/${NEW_MASK} dev $DEFAULT_IFACE"
    if ip addr add "${NEW_IP}/${NEW_MASK}" dev "$DEFAULT_IFACE" 2>&1; then
        ip link set "$DEFAULT_IFACE" up 2>&1 || true
        ip route replace default via "${NEW_GW}" dev "$DEFAULT_IFACE" 2>&1 || \
        ip route add default via "${NEW_GW}" dev "$DEFAULT_IFACE" 2>&1 || true
        echo "✓ ip command fallback succeeded"
        APPLIED=1
    else
        echo "ERROR: ip addr add also failed"
    fi
fi

VERIFY_IP="${ACTUAL_IP:-$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)}"
VERIFY_GW=$(ip route 2>/dev/null | grep '^default' | awk '{print $3}' | head -1)

echo "========== Config Complete =========="
echo "Hostname: $(hostname)"
echo "IP: $VERIFY_IP"
echo "Gateway: $VERIFY_GW"
echo "DNS: $(cat /etc/resolv.conf 2>/dev/null | grep nameserver | head -1)"

if [ "$APPLIED" -eq 1 ]; then
    echo "✓ Network config succeeded"
    exit 0
else
    echo "ERROR: Network config did not take effect"
    exit 1
fi