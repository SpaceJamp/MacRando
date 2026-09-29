using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using NetworkAutoApply = MacRando.NetworkAutoApply;
using NetworkIdentity = MacRando.NetworkIdentity;

/// <summary>
/// Coverage for network-aware presets.
///
/// The safety properties are what matter here, and they are all refusals: an
/// automatic apply must decline whenever the app is not certain that changing an
/// adapter is the right thing to do. Every test below that expects a preset is
/// therefore paired with one that expects a refusal.
/// </summary>
internal static class NetworkPresetTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            NetworkKeyPrefersSsid();
            NetworkKeySeparatesIdenticallyNamedNetworks();
            NetworkKeyIsStableAcrossFormatting();
            NetworkKeyIgnoresMissingParts();
            NetworkKeyCannotBeForgedByTheName();
            NetworkDescriptionIsHumanReadable();
            AdapterKeyIsTakenFromThePresetKey();
            LegacyPresetIsNotBound();
            AutoApplyRequiresAnExactMatch();
            AutoApplyRefusesWhenRestoreDataIsUnreadable();
            AutoApplyRefusesWhileARestoreIsPending();
            AutoApplyRefusesAnEmptyNetworkKey();
            AutoApplyRefusesWhenAutomationIsOff();
            AutoApplyRefusesAPresetWithNoAction();
            AutoApplyHonoursTheCooldown();
            AutoApplyRefusesWhenTwoPresetsAreEligible();
            AutoApplyAllowsOnceTheCooldownHasPassed();
            AutoApplyRefusesWhenThereAreNoPresets();
            AutoApplyIgnoresWhitespaceDifferences();
            BoundPresetDisplaysItsNetwork();
            Console.WriteLine("network-preset-tests=OK;checks=" + _checks);
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

    private static MacRando.AppState StateWith(params MacRando.AdapterPreset[] presets)
    {
        var state = new MacRando.AppState();
        state.Presets = new Dictionary<string, MacRando.AdapterPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (MacRando.AdapterPreset preset in presets)
        {
            state.Presets[preset.PresetKey] = preset;
        }
        return state;
    }

    private static MacRando.AdapterPreset BoundPreset(string key, string networkKey, bool autoApply)
    {
        return new MacRando.AdapterPreset
        {
            PresetKey = "{adapter-guid}::" + key,
            Name = key,
            RandomizeMac = true,
            RandomizeIp = false,
            BindToNetwork = true,
            NetworkKey = networkKey,
            NetworkDescription = "Network, gateway 192.168.1.1",
            AutoApplyOnNetworkChange = autoApply
        };
    }

    private static MacRando.AdapterPreset Select(
        MacRando.AppState state,
        string networkKey,
        int pending,
        bool unreadable,
        DateTime now,
        IDictionary<string, DateTime> lastApplied)
    {
        string reason;
        return NetworkAutoApply.Select(
            state, networkKey, pending, unreadable, now, lastApplied,
            NetworkAutoApply.DefaultCooldownMinutes, out reason);
    }

    private static void NetworkKeyPrefersSsid()
    {
        // Two Wi-Fi networks on different routers can both be called "Network" as a
        // connection profile, so the SSID has to win where one is available.
        string withSsid = NetworkIdentity.BuildKey("Wi-Fi", "192.168.1.1", "Cafe");
        string withoutSsid = NetworkIdentity.BuildKey("Wi-Fi", "192.168.1.1", null);
        Check(withSsid.StartsWith("wifi:", StringComparison.Ordinal), "an SSID should key the network as Wi-Fi");
        Check(withoutSsid.StartsWith("net:", StringComparison.Ordinal), "no SSID should key the network as a profile");
        Check(withSsid.Contains("cafe"), "the SSID should appear in the key");
        Check(!withSsid.Equals(withoutSsid, StringComparison.Ordinal), "SSID and profile keys must differ");
    }

    private static void NetworkKeySeparatesIdenticallyNamedNetworks()
    {
        // The guest-network case: same SSID, different gateway, different place.
        string home = NetworkIdentity.BuildKey("Wi-Fi", "192.168.1.1", "Free WiFi");
        string cafe = NetworkIdentity.BuildKey("Wi-Fi", "10.0.0.1", "Free WiFi");
        Check(!home.Equals(cafe, StringComparison.Ordinal),
            "two networks sharing an SSID but not a gateway must not produce the same key");
    }

    private static void NetworkKeyIsStableAcrossFormatting()
    {
        // Windows is inconsistent about padding and casing between sources, so the key has
        // to normalize or a preset would silently stop matching.
        string a = NetworkIdentity.BuildKey("Network", "192.168.1.1", null);
        string b = NetworkIdentity.BuildKey("  Network  ", " 192.168.1.1 ", null);
        Check(a.Equals(b, StringComparison.Ordinal), "surrounding whitespace must not change the key");
        Check(a == a.ToLowerInvariant(), "the key must be lowercased so casing differences cannot stop a match");
        string cased = NetworkIdentity.BuildKey("NETWORK", "192.168.1.1", null);
        Check(a.Equals(cased, StringComparison.Ordinal), "a differently cased profile must produce the same key");
    }

    private static void NetworkKeyIgnoresMissingParts()
    {
        Check(NetworkIdentity.BuildKey(null, null, null) == string.Empty, "an unknown network has no key");
        Check(NetworkIdentity.BuildKey("", "  ", "") == string.Empty, "blank parts are not a network");
        Check(NetworkIdentity.BuildKey("Network", null, null) ==
                NetworkIdentity.BuildKey("Network", "", null),
            "a missing gateway and a blank gateway are the same absence and must key alike");
        Check(NetworkIdentity.BuildKey(null, "192.168.1.1", null).Length > 0,
            "a gateway alone is enough to identify a network");
    }

    private static void NetworkKeyCannotBeForgedByTheName()
    {
        // The separator is stripped from names so a crafted network name cannot contain a
        // second field and impersonate another key.
        string key = NetworkIdentity.BuildKey("evil|192.168.1.1", "10.0.0.1", null);
        Check(!key.Contains("evil|192.168.1.1|"), "a pipe in the profile name must not survive into the key");
    }

    private static void NetworkDescriptionIsHumanReadable()
    {
        string wifi = NetworkIdentity.BuildDescription("Wi-Fi", "192.168.1.1", "Cafe");
        Check(wifi.Contains("Cafe") && wifi.Contains("192.168.1.1"), "the description should name the network and gateway");
        string wired = NetworkIdentity.BuildDescription("corp.example.com", "10.0.0.1", null);
        Check(wired.Contains("corp.example.com"), "a domain profile should be shown as the network name");
        Check(NetworkIdentity.BuildDescription(null, null, null) == "unknown network",
            "an unknown network should say so rather than showing blanks");
    }

    private static void AdapterKeyIsTakenFromThePresetKey()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:network|192.168.1.1", true);
        Check(preset.AdapterKey == "{adapter-guid}", "the adapter key should be the part before the separator");
        Check(new MacRando.AdapterPreset { Name = "no key" }.AdapterKey == string.Empty,
            "a preset with no key has no adapter");
        Check(new MacRando.AdapterPreset { PresetKey = "::leading" }.AdapterKey == string.Empty,
            "a key with no adapter part should not resolve to an adapter");
        Check(new MacRando.AdapterPreset { PresetKey = "{g}::a::b" }.AdapterKey == "{g}",
            "only the first separator delimits the adapter key");
    }

    private static void LegacyPresetIsNotBound()
    {
        // A 1.4.x preset has none of the network fields, and must load as unbound rather
        // than accidentally matching whatever network the machine is on.
        string legacyJson = "{\"PresetKey\":\"{g}::Home\",\"Name\":\"Home\",\"RandomizeMac\":true,\"RandomizeIp\":false}";
        var preset = new JavaScriptSerializer().Deserialize<MacRando.AdapterPreset>(legacyJson);
        Check(!preset.BindToNetwork, "a legacy preset must not be bound");
        Check(string.IsNullOrEmpty(preset.NetworkKey), "a legacy preset has no network key");
        Check(!preset.AutoApplyOnNetworkChange, "a legacy preset must not auto-apply");
        Check(!preset.IsNetworkBound, "a legacy preset must not report itself as bound");

        MacRando.AdapterPreset flaggedButEmpty = new MacRando.AdapterPreset
        {
            PresetKey = "{adapter-guid}::Flagged",
            Name = "Flagged",
            BindToNetwork = true,
            NetworkKey = null
        };
        Check(!flaggedButEmpty.IsNetworkBound, "a binding flag without a key is not a binding");

        string reason;
        Check(NetworkAutoApply.Select(
                StateWith(flaggedButEmpty), "net:network|192.168.1.1", 0, false,
                DateTime.UtcNow, null, 10, out reason) == null,
            "a preset with no network key must never match");
    }

    private static void AutoApplyRequiresAnExactMatch()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        MacRando.AdapterPreset found = Select(StateWith(preset), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null);
        Check(found != null && found.Name == "Home", "a bound preset should apply on its own network");
        Check(Select(StateWith(preset), "net:other|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "a preset must not apply on a network it is not bound to");
    }

    private static void AutoApplyRefusesWhenRestoreDataIsUnreadable()
    {
        // The single most important refusal: if MacRando cannot read the profile that would
        // put an adapter back, it must not create a new change to keep.
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, true, DateTime.UtcNow, null) == null,
            "unreadable restore data must block an automatic apply");
    }

    private static void AutoApplyRefusesWhileARestoreIsPending()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 1, false, DateTime.UtcNow, null) == null,
            "a pending restore must block an automatic apply");
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) != null,
            "no pending restore should allow the apply");
    }

    private static void AutoApplyRefusesAnEmptyNetworkKey()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        Check(Select(StateWith(preset), "", 0, false, DateTime.UtcNow, null) == null,
            "an unidentified network must not trigger an apply");
        Check(Select(StateWith(preset), "   ", 0, false, DateTime.UtcNow, null) == null,
            "a blank network key must not trigger an apply");
    }

    private static void AutoApplyRefusesWhenAutomationIsOff()
    {
        // Binding a preset records where it belongs; it must not by itself arm it.
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", false);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "a bound preset with automatic apply off must not run");
    }

    private static void AutoApplyRefusesAPresetWithNoAction()
    {
        MacRando.AdapterPreset preset = BoundPreset("Empty", "net:home|192.168.1.1", true);
        preset.RandomizeMac = false;
        preset.RandomizeIp = false;
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "a bound preset with no action must not run");
    }

    private static void AutoApplyHonoursTheCooldown()
    {
        // A gateway that flaps would otherwise re-randomize the adapter repeatedly.
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        DateTime now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var last = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        last[preset.PresetKey] = now.AddMinutes(-2);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, now, last) == null,
            "a preset applied two minutes ago must still be cooling down");

        // The cooldown is per preset, so it must not block a different bound preset.
        MacRando.AdapterPreset other = BoundPreset("Office", "net:home|192.168.1.1", true);
        MacRando.AdapterPreset found = Select(StateWith(preset, other), "net:home|192.168.1.1", 0, false, now, last);
        Check(found != null && found.Name == "Office", "a different preset must not inherit the cooldown");

        // A clock that jumps backwards must not produce a negative cooldown forever.
        var future = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        future[preset.PresetKey] = now.AddMinutes(30);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, now, future) != null,
            "a timestamp in the future must not lock the preset out indefinitely");
    }

    private static void AutoApplyRefusesWhenTwoPresetsAreEligible()
    {
        // Two presets armed for the same network is a configuration mistake. Picking one
        // would mean the adapter changed in a way the user did not ask for, so nothing runs
        // and the log says why.
        MacRando.AdapterPreset home = BoundPreset("Home", "net:home|192.168.1.1", true);
        MacRando.AdapterPreset spare = BoundPreset("Spare", "net:home|192.168.1.1", true);
        string reason;
        MacRando.AdapterPreset found = NetworkAutoApply.Select(
            StateWith(home, spare), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null,
            NetworkAutoApply.DefaultCooldownMinutes, out reason);
        Check(found == null, "two eligible presets for one network must not resolve to a guess");
        Check(reason.Contains("more than one"), "the refusal should say the presets conflict");

        // The conflict is only resolved when one of them is taken out of the running.
        MacRando.AdapterPreset off = BoundPreset("Spare", "net:home|192.168.1.1", false);
        found = NetworkAutoApply.Select(
            StateWith(home, off), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null,
            NetworkAutoApply.DefaultCooldownMinutes, out reason);
        Check(found != null && found.Name == "Home", "a disarmed preset must not block the armed one");
    }

    private static void AutoApplyAllowsOnceTheCooldownHasPassed()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "net:home|192.168.1.1", true);
        DateTime now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var last = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        last[preset.PresetKey] = now.AddMinutes(-NetworkAutoApply.DefaultCooldownMinutes - 1);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, now, last) != null,
            "the preset should apply again once the cooldown has elapsed");
    }

    private static void AutoApplyRefusesWhenThereAreNoPresets()
    {
        Check(Select(new MacRando.AppState(), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "no presets must not apply");
        MacRando.AppState nullPresets = new MacRando.AppState();
        Check(Select(nullPresets, "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "a null preset collection must not apply");
        Check(Select(null, "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) == null,
            "a null state must not apply");
    }

    private static void AutoApplyIgnoresWhitespaceDifferences()
    {
        MacRando.AdapterPreset preset = BoundPreset("Home", "  net:home|192.168.1.1  ", true);
        Check(Select(StateWith(preset), "net:home|192.168.1.1", 0, false, DateTime.UtcNow, null) != null,
            "surrounding whitespace in a stored key must not stop the match");
    }

    private static void BoundPresetDisplaysItsNetwork()
    {
        MacRando.AdapterPreset bound = BoundPreset("Home", "net:home|192.168.1.1", true);
        Check(bound.ToString().Contains("192.168.1.1"),
            "a bound preset should show its network in the list so the binding is visible");
        MacRando.AdapterPreset unbound = BoundPreset("Portable", "net:home|192.168.1.1", true);
        unbound.BindToNetwork = false;
        Check(!unbound.ToString().Contains("192.168.1.1"),
            "an unbound preset should not advertise a network");
        Check(new MacRando.AdapterPreset().ToString() == "Unnamed preset",
            "a nameless preset should still render");
    }
}
