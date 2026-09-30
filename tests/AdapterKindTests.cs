using System;
using System.Collections.Generic;
using MacRando;

/// <summary>
/// Coverage for adapter classification.
///
/// The behaviour under test is a refusal, so the cases that matter most are the ones
/// where the answer must be "no": a tunnel or a virtual machine host adapter arriving in
/// the list and being treated like real hardware. The earlier filter, "Get-NetAdapter
/// -Physical and not .Virtual", already covered the adapters Windows flags, which is why
/// this exists at all: the ones it misses are exactly the ones that register a real NDIS
/// miniport and are not flagged.
/// </summary>
internal static class AdapterKindTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            RealHardwareIsChangeable();
            VirtualMachineAdaptersAreRecognised();
            TunnelsAreRecognised();
            TunnelBeatsVirtualMachine();
            OtherVirtualAdaptersAreRecognised();
            ClassificationIsCaseInsensitive();
            EveryRestrictedKindExplainsItself();
            TheApplyGuardRefusesWhatClassificationRefuses();
            TheListMarksOffLimitsAdapters();
            Console.WriteLine("adapter-kind-tests=OK;checks=" + _checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static AdapterKind Classify(string name, string description)
    {
        return AdapterClassification.Classify(name, description);
    }

    private static void RealHardwareIsChangeable()
    {
        // Taken from this machine, so the real drivers are covered rather than invented ones.
        Check(Classify("Ethernet", "Realtek Gaming 2.5GbE Family Controller") == AdapterKind.Physical,
            "a Realtek wired adapter must be physical");
        Check(Classify("Wi-Fi", "RZ616 Wi-Fi 6E 160MHz") == AdapterKind.Physical,
            "a Wi-Fi adapter must be physical");

        Check(AdapterClassification.IsChangeable(AdapterKind.Physical),
            "a physical adapter must be changeable");

        // The empty and null cases, because a driver that reports nothing must not be
        // classified as anything but hardware, or a real adapter would be refused.
        Check(Classify(null, null) == AdapterKind.Physical,
            "an adapter with no name and no description must default to physical");
        Check(Classify("", "") == AdapterKind.Physical,
            "empty strings must default to physical");

        // An adapter whose name is a plain word must not be caught by an over-broad marker.
        Check(Classify("Ethernet 2", "Intel(R) Ethernet Connection (7) I219-V") == AdapterKind.Physical,
            "an Intel wired adapter must be physical");
        Check(Classify("Local Area Connection", "Broadcom NetXtreme Fast Ethernet") == AdapterKind.Physical,
            "a Broadcom wired adapter must be physical");
    }

    private static void VirtualMachineAdaptersAreRecognised()
    {
        // Each of these registers a real NDIS miniport on Windows, so Windows does not
        // flag them Virtual. That is the whole reason this classification exists.
        string[] names =
        {
            "vEthernet (Default Switch)",
            "vEthernet (WSL)",
            "Ethernet 2",
            "VMware Network Adapter VMnet1",
            "VMware Network Adapter VMnet8",
            "VirtualBox Host-Only Ethernet Adapter"
        };
        string[] descriptions =
        {
            "Hyper-V Virtual Ethernet Adapter",
            "Hyper-V Virtual Ethernet Adapter",
            "Hyper-V Network Adapter",
            "VMware Virtual Ethernet Adapter for VMnet1",
            "VMware Virtual Ethernet Adapter for VMnet8",
            "VirtualBox Host-Only Ethernet Adapter"
        };
        for (int index = 0; index < names.Length; index++)
        {
            AdapterKind kind = Classify(names[index], descriptions[index]);
            Check(kind == AdapterKind.VirtualMachine,
                "\"" + names[index] + " / " + descriptions[index] + "\" should be a virtual machine adapter, but was " + kind);
            Check(!AdapterClassification.IsChangeable(kind),
                "\"" + names[index] + "\" must not be changeable");
        }

        // The name alone is enough, because Hyper-V names are distinctive and a host may
        // report an empty or generic driver description.
        Check(Classify("vEthernet (Default Switch)", null) == AdapterKind.VirtualMachine,
            "a Hyper-V name with no driver description must still be recognised");
        Check(Classify(null, "Hyper-V Virtual Ethernet Adapter") == AdapterKind.VirtualMachine,
            "a Hyper-V driver with no adapter name must still be recognised");
    }

    private static void TunnelsAreRecognised()
    {
        // Third-party tunnels, the ones MacRando knows nothing about and so cannot restore.
        string[][] thirdParty =
        {
            new[] { "WireGuard Tunnel", "WireGuard Tunnel" },
            new[] { "WARP", "Cloudflare WARP Adapter" },
            new[] { "Tailscale", "Tailscale Tunnel" },
            new[] { "ZeroTier One [network id]", "ZeroTier" },
            new[] { "OpenVPN TAP Adapter", "OpenVPN TAP" },
            new[] { "NordLynx", "NordLynx Tunnel" },
            new[] { "Ethernet 3", "Cloudflare WARP" }
        };
        for (int index = 0; index < thirdParty.Length; index++)
        {
            AdapterKind kind = Classify(thirdParty[index][0], thirdParty[index][1]);
            Check(kind == AdapterKind.Tunnel,
                "\"" + thirdParty[index][0] + "\" should be a tunnel, but was " + kind);
            Check(!AdapterClassification.IsChangeable(kind),
                "\"" + thirdParty[index][0] + "\" must not be changeable");
        }

        // Windows' own VPN adapters. MacRando can drive these through rasdial, but the
        // adapter is the tunnel's, and changing its MAC mid-connection is a good way to
        // produce an unexplained drop.
        string[][] windows =
        {
            new[] { "IKEv2 Adapter", "Microsoft IKEv2 Adapter" },
            new[] { "VPN Adapter", "VPN Miniport Adapter" },
            new[] { "Ethernet 4", "Microsoft L2TP Adapter" }
        };
        for (int index = 0; index < windows.Length; index++)
        {
            AdapterKind kind = Classify(windows[index][0], windows[index][1]);
            Check(kind == AdapterKind.Tunnel,
                "\"" + windows[index][0] + "\" should be a tunnel, but was " + kind);
        }
    }

    private static void TunnelBeatsVirtualMachine()
    {
        // A tunnel on a virtualised host is genuinely both. The tunnel reading is the one
        // that carries the warning worth showing, so the order is asserted rather than
        // left to whichever list is searched first.
        Check(Classify("vEthernet (WSL)", "WireGuard Tunnel") == AdapterKind.Tunnel,
            "a tunnel on a virtual host must classify as a tunnel");
        Check(Classify("Docker Tunnel", "Hyper-V Virtual Ethernet Adapter") == AdapterKind.Tunnel,
            "a Docker tunnel on Hyper-V must classify as a tunnel");
    }

    private static void OtherVirtualAdaptersAreRecognised()
    {
        Check(Classify("Bluetooth Network Connection", "Bluetooth Device (Personal Area Network)") == AdapterKind.OtherVirtual,
            "a Bluetooth PAN should be classified as other-virtual");
        Check(!AdapterClassification.IsChangeable(AdapterKind.OtherVirtual),
            "a Bluetooth PAN must not be changeable");
    }

    private static void ClassificationIsCaseInsensitive()
    {
        // Windows is inconsistent about casing in driver strings, so this is a real risk
        // rather than a theoretical one.
        Check(Classify("VETHERNET (DEFAULT SWITCH)", "HYPER-V VIRTUAL ETHERNET ADAPTER") == AdapterKind.VirtualMachine,
            "classification must ignore case in both name and description");
        Check(Classify("wireguard tunnel", "WIREGUARD TUNNEL") == AdapterKind.Tunnel,
            "a lower-cased tunnel must still be recognised");
    }

    private static void EveryRestrictedKindExplainsItself()
    {
        // A kind with an empty explanation would show an adapter MacRando will refuse
        // with no stated reason, which is the failure mode this is meant to prevent.
        foreach (AdapterKind kind in new[] { AdapterKind.VirtualMachine, AdapterKind.Tunnel, AdapterKind.OtherVirtual })
        {
            string reason = AdapterClassification.DescribeRestriction(kind);
            Check(!string.IsNullOrWhiteSpace(reason),
                kind + " must explain why it is off limits");
            // Ends in a period, so it reads as a sentence when appended after a name.
            Check(reason.TrimEnd().EndsWith(".", StringComparison.Ordinal),
                "the reason for " + kind + " should be a complete sentence: " + reason);

            string label = AdapterClassification.DescribeKind(kind);
            Check(!string.IsNullOrWhiteSpace(label), kind + " must have a short label");
        }

        Check(AdapterClassification.DescribeRestriction(AdapterKind.Physical) == string.Empty,
            "a physical adapter must have no restriction text");
        Check(AdapterClassification.DescribeKind(AdapterKind.Physical) == "physical",
            "a physical adapter must be labelled physical");
    }

    private static void TheApplyGuardRefusesWhatClassificationRefuses()
    {
        var tunnel = new AdapterInfo { Name = "WireGuard Tunnel", Description = "WireGuard Tunnel", IsUp = true };
        var hyperV = new AdapterInfo { Name = "vEthernet (Default Switch)", Description = "Hyper-V Virtual Ethernet Adapter", IsUp = true };
        var ethernet = new AdapterInfo { Name = "Ethernet", Description = "Realtek Gaming 2.5GbE Family Controller" };

        // This is the guard the network-change watcher actually calls, and the only thing
        // standing between a stale preset and a tunnel adapter on a path where no button
        // in the interface is disabled.
        Check(!MacRando.DashboardForm.CanAutoApplyTo(tunnel),
            "auto-apply must refuse a connected tunnel adapter");
        Check(!MacRando.DashboardForm.CanAutoApplyTo(hyperV),
            "auto-apply must refuse a connected virtual machine adapter");

        // Connected is not enough on its own, and neither is being physical: both
        // conditions are required, so neither regression is possible on its own.
        Check(!MacRando.DashboardForm.CanAutoApplyTo(ethernet),
            "auto-apply must refuse a physical adapter that is down");
        Check(MacRando.DashboardForm.CanAutoApplyTo(new AdapterInfo
        {
            Name = "Ethernet",
            Description = "Realtek Gaming 2.5GbE Family Controller",
            IsUp = true
        }), "auto-apply must accept a connected physical adapter");
        Check(!MacRando.DashboardForm.CanAutoApplyTo(null),
            "auto-apply must refuse a missing adapter");

        // And the same restriction must reach the interface eligibility check, which is
        // what disables the buttons.
        Check(!MacRando.DashboardForm.IsLocalIpEligible(
                new AdapterInfo
                {
                    Name = "WireGuard Tunnel",
                    Description = "WireGuard Tunnel",
                    IsUp = true,
                    IpAddress = "10.4.0.2",
                    PrefixLength = 24
                },
                false),
            "a tunnel with a perfectly good IPv4 address must still be ineligible for local IP randomization");
    }

    private static void TheListMarksOffLimitsAdapters()
    {
        // The label is what tells a user why their adapter is greyed out, so it is
        // asserted as a value rather than trusted to be present.
        Check(AdapterClassification.DescribeKind(AdapterKind.Tunnel) == "tunnel",
            "a tunnel must be labelled tunnel for the adapter list");
        Check(AdapterClassification.DescribeKind(AdapterKind.VirtualMachine) == "virtual machine",
            "a virtual machine adapter must be labelled for the adapter list");

        // Restoring is not blocked by this, deliberately: MacRando must always be able to
        // put back something it changed, and it did not change an adapter it refuses to
        // touch. Only creation of new changes is refused.
        var adapter = new AdapterInfo
        {
            Name = "WireGuard Tunnel",
            Description = "WireGuard Tunnel",
            MacPropertySupported = true
        };
        Check(adapter.Restriction.Length > 0,
            "AdapterInfo must surface the restriction, not only the enum");
        Check(!adapter.IsChangeable,
            "AdapterInfo.IsChangeable must agree with the classification");
    }
}
