using System;
using System.Collections.Generic;

namespace MacRando
{
    /// <summary>
    /// What kind of thing an adapter is, and whether MacRando should touch it.
    ///
    /// The existing filter, "Get-NetAdapter -Physical and not .Virtual", is not enough on
    /// its own. It drops most virtual adapters, but a virtual machine host adapter or a
    /// third-party tunnel often registers a genuine NDIS miniport and is not flagged
    /// Virtual at all, so it arrives in the list looking exactly like real hardware.
    /// Randomising one of those is at best meaningless and at worst disruptive: on a
    /// Hyper-V or VMware host adapter it changes the identity the host's guests see, and
    /// on a tunnel adapter it disrupts the tunnel MacRando does not own and cannot restore.
    ///
    /// This classifies rather than hides. Nothing is removed from the list, because an
    /// adapter that silently disappears is worse than one that says why it is off limits,
    /// and the driver string is often the only clue about what a machine is actually
    /// running. The classification is advisory in the list and enforced at the point of
    /// change, so a classification mistake cannot strand a real adapter.
    /// </summary>
    internal enum AdapterKind
    {
        /// <summary>Real hardware. Normal operation.</summary>
        Physical,

        /// <summary>A VM host or guest bridge: Hyper-V, VMware, VirtualBox, and similar.</summary>
        VirtualMachine,

        /// <summary>A VPN or tunnel adapter, whether or not it is one of ours.</summary>
        Tunnel,

        /// <summary>A loopback, bridge, or other adapter that is not a network device.</summary>
        OtherVirtual
    }

    internal static class AdapterClassification
    {
        /// <summary>
        /// Driver and name fragments that identify a virtual machine host or guest bridge.
        ///
        /// Matched as substrings, case-insensitively, against the adapter name and driver
        /// description. Kept as data rather than compiled in so a new hypervisor is a
        /// one-line change and the list can be shown to the user.
        /// </summary>
        private static readonly string[] VirtualMachineMarkers =
        {
            "hyper-v",
            "hyperv",
            "vethernet",
            "vmware",
            "virtualbox",
            "vbox",
            "virtual machine",
            "parallels",
            "qemu",
            "kvm",
            "xen",
            "bhyve",
            "utm",
            "docker",
            "wsl"
        };

        /// <summary>
        /// Driver and name fragments that identify a VPN or tunnel adapter.
        ///
        /// Windows' own IKEv2/L2TP adapters are included deliberately: MacRando can drive
        /// those through rasdial, but the adapter itself is the tunnel's, and changing its
        /// MAC mid-tunnel is the kind of thing that shows up as an unexplained drop.
        /// </summary>
        private static readonly string[] TunnelMarkers =
        {
            "wireguard",
            "warp",
            "cloudflare",
            "tailscale",
            "zerotier",
            "openvpn",
            "nordvpn",
            "expressvpn",
            "protonvpn",
            "mullvad",
            "surfshark",
            "cyberghost",
            "private internet access",
            "pia ",
            "sophos",
            "forticlient",
            "fortinet",
            "cisco anyconnect",
            "anyconnect",
            "globalprotect",
            "pulse secure",
            "windscribe",
            "tunnel",
            "tap-",
            "tap ",
            "tun",
            "vpn",
            "ikev2",
            "l2tp",
            "pptp",
            "sstp"
        };

        /// <summary>Fragments identifying a loopback or similar non-device.</summary>
        private static readonly string[] OtherVirtualMarkers =
        {
            "loopback",
            "npcap",
            "microsoft km-test",
            "bluetooth device (personal area network)"
        };

        /// <summary>
        /// Classifies one adapter from its name and driver description.
        ///
        /// Tunnel is checked before virtual machine because a virtualised gateway such as
        /// a WireGuard interface on a Hyper-V host is genuinely both, and the tunnel
        /// reading is the one that carries the warning worth showing.
        /// </summary>
        public static AdapterKind Classify(string name, string description)
        {
            string text = ((name ?? string.Empty) + " " + (description ?? string.Empty)).ToLowerInvariant();
            if (Matches(text, TunnelMarkers))
            {
                return AdapterKind.Tunnel;
            }
            if (Matches(text, VirtualMachineMarkers))
            {
                return AdapterKind.VirtualMachine;
            }
            if (Matches(text, OtherVirtualMarkers))
            {
                return AdapterKind.OtherVirtual;
            }
            return AdapterKind.Physical;
        }

        private static bool Matches(string text, string[] markers)
        {
            for (int index = 0; index < markers.Length; index++)
            {
                if (text.IndexOf(markers[index], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether MacRando may change this kind of adapter.</summary>
        public static bool IsChangeable(AdapterKind kind)
        {
            return kind == AdapterKind.Physical;
        }

        /// <summary>
        /// Why an adapter is off limits, phrased for a user rather than a developer.
        ///
        /// Returns an empty string when the adapter may be changed, so callers can use it
        /// directly as a tooltip or a disabled reason without a second condition.
        /// </summary>
        public static string DescribeRestriction(AdapterKind kind)
        {
            switch (kind)
            {
                case AdapterKind.VirtualMachine:
                    return "This adapter belongs to a virtual machine or container host. " +
                        "Changing it affects the guests behind it, not this machine's own connection.";
                case AdapterKind.Tunnel:
                    return "This adapter is a VPN or tunnel. Changing it can break the tunnel, " +
                        "and MacRando does not own the tunnel's configuration, so it cannot put it right.";
                case AdapterKind.OtherVirtual:
                    return "This adapter is a virtual or loopback interface and has no hardware address to change.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>Short label for the adapter list, so the kind is visible at a glance.</summary>
        public static string DescribeKind(AdapterKind kind)
        {
            switch (kind)
            {
                case AdapterKind.VirtualMachine:
                    return "virtual machine";
                case AdapterKind.Tunnel:
                    return "tunnel";
                case AdapterKind.OtherVirtual:
                    return "virtual";
                default:
                    return "physical";
            }
        }
    }
}
