#!/bin/bash
set +e

NEW_IP="${IP}"
NEW_MASK="${MASK}"
NEW_GW="${GW}"
NEW_DNS="${DNS}"
NEW_HOST="${HOSTNAME}"

echo "========== GuestOps Network Config (Linux) =========="
echo "IP='$NEW_IP'  Mask='$NEW_MASK'  GW='$NEW_GW'  DNS='$NEW_DNS'  Host='$NEW_HOST'"

if ! pgrep -f vmtoolsd > /dev/null 2>&1 && ! pgrep -f vmware-toolbox > /dev/null 2>&1; then
    echo "WARNING: VMware Tools process not detected"
fi

is_virtual_iface() {
    case "$1" in
        lo|docker0|br-*|veth*|netns*|tap*|tun*|vmnet*|virbr*|vnet*|wg*|tailscale*|flannel*|cni*|cni0|lxcbr*|vboxnet*|hyperv*)
            return 0 ;;
    esac
    return 1
}

is_physical_iface() {
    case "$1" in
        eth*|ens*|enp*|eno*|em*|p2p*|eno*|ixgbe*|bnx*|tg3*)
            return 0 ;;
    esac
    return 1
}

echo "--- Available interfaces ---"
ip -br link 2>/dev/null | while read line; do
    iface=$(echo "$line" | awk '{print $1}')
    state=$(echo "$line" | awk '{print $2}')
    vskip=""; pmark=""
    is_virtual_iface "$iface" && vskip=" [SKIP: virtual/bridge]"
    is_physical_iface "$iface" && pmark=" *physical"
    echo "  $iface  state=$state$pmark$vskip"
done

DEFAULT_IFACE=""

DEFAULT_IFACE=$(ip -br link 2>/dev/null | awk '$2 ~ /UP/ {print $1}' | while read iface; do
    is_virtual_iface "$iface" && continue
    is_physical_iface "$iface" && echo "$iface" && break
done)

if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip -br link 2>/dev/null | awk '$2 ~ /UP/ {print $1}' | while read iface; do
        is_virtual_iface "$iface" && continue
        echo "$iface"; break
    done)
fi

if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip -br link 2>/dev/null | awk '{print $1}' | while read iface; do
        is_virtual_iface "$iface" && continue
        is_physical_iface "$iface" && echo "$iface" && break
    done)
fi

if [ -z "$DEFAULT_IFACE" ]; then
    DEFAULT_IFACE=$(ip -br link 2>/dev/null | awk '{print $1}' | while read iface; do
        is_virtual_iface "$iface" && continue
        echo "$iface"; break
    done)
fi

echo "Selected interface: $DEFAULT_IFACE"

if [ -z "$DEFAULT_IFACE" ]; then
    echo "ERROR: No usable interface detected"
    exit 1
fi

if [ -f /etc/os-release ]; then
    . /etc/os-release
    echo "Distro: $PRETTY_NAME (${ID:-unknown})"
fi

CHANGED=0

if [ -n "$NEW_HOST" ]; then
    echo "-> Setting hostname: $NEW_HOST"
    hostnamectl set-hostname "$NEW_HOST" 2>/dev/null || hostname "$NEW_HOST"
    echo "$NEW_HOST" > /etc/hostname 2>/dev/null || true
    sed -i "s/^127\.0\.0\.1.*/127.0.0.1 localhost $NEW_HOST/" /etc/hosts 2>/dev/null || true
    CHANGED=1
else
    echo "-> Hostname: skip (empty)"
fi

if [ -n "$NEW_DNS" ]; then
    echo "-> Setting DNS: $NEW_DNS"
    echo "nameserver $NEW_DNS" > /etc/resolv.conf
    if [ -d /etc/resolvconf/resolv.conf.d/head ]; then
        echo "nameserver $NEW_DNS" > /etc/resolvconf/resolv.conf.d/head
        resolvconf -u 2>/dev/null || true
    fi
    if [ -d /etc/NetworkManager ]; then
        DNS_CONN=$(nmcli -t -f NAME,DEVICE con show --active 2>/dev/null | grep ":${DEFAULT_IFACE}$" | head -1 | cut -d: -f1)
        [ -n "$DNS_CONN" ] && nmcli con mod "$DNS_CONN" ipv4.dns "$NEW_DNS" 2>/dev/null
    fi
    CHANGED=1
else
    echo "-> DNS: skip (empty)"
fi

if [ -z "$NEW_IP" ] && [ -z "$NEW_GW" ]; then
    echo "-> IP/GW: both empty, skip network config entirely"
else
    APPLIED=0
    MASK_VAL="${NEW_MASK:-24}"

    if command -v nmcli > /dev/null 2>&1; then
        echo "-> NetworkManager (nmcli) detected"
        CONN=$(nmcli -t -f NAME,DEVICE con show 2>/dev/null | grep ":${DEFAULT_IFACE}$" | head -1 | cut -d: -f1)
        if [ -z "$CONN" ]; then
            echo "  No existing connection bound to $DEFAULT_IFACE, creating one"
            CONN="guestops-${DEFAULT_IFACE}"
            nmcli con add type ethernet ifname "$DEFAULT_IFACE" con-name "$CONN" 2>/dev/null || true
        fi
        if [ -n "$CONN" ]; then
            echo "  Using connection '$CONN' bound to $DEFAULT_IFACE"
            if [ -n "$NEW_IP" ]; then
                nmcli con mod "$CONN" ipv4.method manual ipv4.addresses "${NEW_IP}/${MASK_VAL}" 2>/dev/null
            fi
            if [ -n "$NEW_GW" ]; then
                nmcli con mod "$CONN" ipv4.gateway "$NEW_GW" 2>/dev/null
            fi
            if [ -n "$NEW_DNS" ]; then
                nmcli con mod "$CONN" ipv4.dns "$NEW_DNS" 2>/dev/null
            fi
            nmcli con mod "$CONN" connection.interface-name "$DEFAULT_IFACE" 2>/dev/null || true
            if nmcli con up "$CONN" 2>&1; then
                sleep 2
                ACTUAL_IP=$(nmcli -g ipv4.addresses con show "$CONN" 2>/dev/null | cut -d/ -f1 | head -1)
                if [ -n "$ACTUAL_IP" ]; then
                    echo "[OK] nmcli succeeded, IP=$ACTUAL_IP"; APPLIED=1
                else
                    echo "WARNING: nmcli con up OK but IP empty"
                fi
            else
                echo "WARNING: nmcli con up failed"
            fi
        else
            echo "WARNING: nmcli but no connection, falling back to ip"
        fi

    elif command -v netplan > /dev/null 2>&1 && ls /etc/netplan/*.yaml > /dev/null 2>&1; then
        echo "-> netplan detected"
        NETPLAN_FILE="/etc/netplan/99-guestops.yaml"
        cat > "$NETPLAN_FILE" <<EOF
network:
  version: 2
  ethernets:
    ${DEFAULT_IFACE}:
      dhcp4: no
EOF
        [ -n "$NEW_IP" ] && echo "      addresses: [${NEW_IP}/${MASK_VAL}]" >> "$NETPLAN_FILE"
        [ -n "$NEW_GW" ] && echo "      gateway4: ${NEW_GW}" >> "$NETPLAN_FILE"
        if [ -n "$NEW_DNS" ]; then
            echo "      nameservers:" >> "$NETPLAN_FILE"
            echo "        addresses: [${NEW_DNS}]" >> "$NETPLAN_FILE"
        fi
        netplan apply 2>/dev/null
        if [ $? -eq 0 ]; then
            sleep 2
            ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
            if [ -n "$ACTUAL_IP" ]; then
                echo "[OK] netplan apply succeeded, IP=$ACTUAL_IP"; APPLIED=1
            fi
        fi

    elif ls /etc/sysconfig/network-scripts/ifcfg-* > /dev/null 2>&1; then
        echo "-> ifcfg detected"
        IFCFG_FILE="/etc/sysconfig/network-scripts/ifcfg-${DEFAULT_IFACE}"
        [ -n "$NEW_IP" ] && sed -i "s/^IPADDR=.*/IPADDR=${NEW_IP}/" "$IFCFG_FILE" 2>/dev/null || echo "IPADDR=${NEW_IP}" >> "$IFCFG_FILE"
        [ -n "$NEW_GW" ] && sed -i "s/^GATEWAY=.*/GATEWAY=${NEW_GW}/" "$IFCFG_FILE" 2>/dev/null || echo "GATEWAY=${NEW_GW}" >> "$IFCFG_FILE"
        [ -n "$NEW_MASK" ] && sed -i "s/^PREFIX=.*/PREFIX=${MASK_VAL}/" "$IFCFG_FILE" 2>/dev/null
        ifup "$DEFAULT_IFACE" 2>/dev/null || systemctl restart network 2>/dev/null || systemctl restart NetworkManager 2>/dev/null || true
        sleep 2
        ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
        [ -n "$ACTUAL_IP" ] && { echo "[OK] ifcfg succeeded, IP=$ACTUAL_IP"; APPLIED=1; }

    elif [ -f /etc/network/interfaces ] || command -v ifup > /dev/null 2>&1; then
        echo "-> /etc/network/interfaces detected"
        if [ -n "$NEW_IP" ]; then
            sed -i "s/^[[:space:]]*address .*/    address ${NEW_IP}\/${MASK_VAL}/" /etc/network/interfaces 2>/dev/null
        fi
        [ -n "$NEW_GW" ] && sed -i "s/^[[:space:]]*gateway .*/    gateway ${NEW_GW}/" /etc/network/interfaces 2>/dev/null
        ifdown "$DEFAULT_IFACE" 2>/dev/null
        ifup "$DEFAULT_IFACE" 2>/dev/null
        sleep 2
        ACTUAL_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
        [ -n "$ACTUAL_IP" ] && { echo "[OK] interfaces succeeded, IP=$ACTUAL_IP"; APPLIED=1; }
    fi

    if [ "$APPLIED" -eq 0 ]; then
        echo "-> Using ip command fallback"
        if [ -n "$NEW_IP" ]; then
            ip addr flush dev "$DEFAULT_IFACE" 2>&1 || true
            if ip addr add "${NEW_IP}/${MASK_VAL}" dev "$DEFAULT_IFACE" 2>&1; then
                ip link set "$DEFAULT_IFACE" up 2>&1 || true
                echo "[OK] ip addr add succeeded"
            else
                echo "ERROR: ip addr add failed"
            fi
        fi
        if [ -n "$NEW_GW" ]; then
            ip route replace default via "${NEW_GW}" dev "$DEFAULT_IFACE" 2>&1 || \
            ip route add default via "${NEW_GW}" dev "$DEFAULT_IFACE" 2>&1 || true
            echo "[OK] gateway set"
        fi
    fi
    CHANGED=1
fi

VERIFY_IP=$(ip addr show "$DEFAULT_IFACE" 2>/dev/null | grep -E 'inet ' | awk '{print $2}' | cut -d/ -f1 | head -1)
VERIFY_GW=$(ip route 2>/dev/null | grep '^default' | awk '{print $3}' | head -1)

echo "========== Config Complete =========="
echo "Hostname: $(hostname)"
echo "IP: ${VERIFY_IP:-unchanged}"
echo "Gateway: ${VERIFY_GW:-unchanged}"
echo "DNS: $(cat /etc/resolv.conf 2>/dev/null | grep nameserver | head -1)"

exit 0