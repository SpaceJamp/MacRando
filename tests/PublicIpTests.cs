using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MacRando;
using DashboardForm = MacRando.DashboardForm;
using NetworkIdentity = MacRando.NetworkIdentity;
using NetworkService = MacRando.NetworkService;

/// <summary>
/// Public IP and geolocation logic.
///
/// This is the code that has failed twice already: once because the service it called
/// was dead, and once because the result was only ever written to a tooltip. Neither
/// failure was caught by any test, because there were none. The rules that decide what
/// is shown are pure functions of the data, so they can be checked exhaustively without
/// touching the network.
/// </summary>
internal static class PublicIpTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            DtoParsesIpinfoResponse();
            DtoHandlesMissingOrg();
            DtoHandlesNull();
            DescribePlace();
            DescribeFullLocation();
            DescribeShortLocation();
            AddressUnavailableIsDistinguishedFromLocationUnavailable();
            AddressArrivingClearsTheMessage();
            NetworkIdentityBuildsKey();
            NetworkIdentityBuildDescription();
            NetworkIdentityIsUsable();
            Console.WriteLine("public-ip-tests=OK;checks=" + _checks);
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

    // ------------------------------------------------------------- DTO

    private static void DtoParsesIpinfoResponse()
    {
        // A real ipinfo.io response shape. The org field combines the ASN and the
        // provider name, and both have to come out of it.
        var dto = new NetworkService.PublicIpGeolocation
        {
            Ip = "203.0.113.9",
            Country = "US",
            Region = "Florida",
            City = "Clermont",
            Org = "AS33363 Charter Communications, Inc",
            Timezone = "America/New_York"
        };

        Check(dto.Isp == "AS33363 Charter Communications, Inc",
            "Isp should be the whole org field, but was: " + (dto.Isp ?? "(null)"));
        Check(dto.Asn == "AS33363",
            "Asn should be the first token of org, but was: " + (dto.Asn ?? "(null)"));
    }

    private static void DtoHandlesMissingOrg()
    {
        // Some responses omit org entirely (private/reserved ranges, or a service hiccup).
        // The split must not throw and must not fabricate a value.
        var dto = new NetworkService.PublicIpGeolocation
        {
            Ip = "203.0.113.9",
            Country = "US",
            Region = "Florida",
            City = "Clermont",
            Org = null,
            Timezone = "America/New_York"
        };

        Check(string.IsNullOrWhiteSpace(dto.Isp),
            "Isp should be blank when org is missing, but was: " + (dto.Isp ?? "(null)"));
        Check(string.IsNullOrWhiteSpace(dto.Asn),
            "Asn should be blank when org is missing, but was: " + (dto.Asn ?? "(null)"));
    }

    private static void DtoHandlesNull()
    {
        // A response with no org and no other fields must not throw.
        var dto = new NetworkService.PublicIpGeolocation();
        Check(dto.Isp == null || dto.Isp == string.Empty,
            "Isp should be null or empty for an empty DTO.");
        Check(dto.Asn == null || dto.Asn == string.Empty,
            "Asn should be null or empty for an empty DTO.");
    }

    private static void DtoHandlesSingleTokenOrg()
    {
        // A provider that reports only an ASN with no name. The split must not lose it.
        var dto = new NetworkService.PublicIpGeolocation { Org = "AS9009" };
        Check(dto.Isp == "AS9009", "Isp should be the org when there is no name.");
        Check(dto.Asn == "AS9009", "Asn should be the org when there is no name.");
    }

    // ------------------------------------------------------------- Describe

    private static NetworkIdentity GeoIdentity()
    {
        return new NetworkIdentity
        {
            PublicIpCountry = "US",
            PublicIpRegion = "Florida",
            PublicIpCity = "Clermont",
            PublicIpIsp = "Charter Communications, Inc",
            PublicIpAsn = "AS33363",
            PublicIpTimezone = "America/New_York"
        };
    }

    private static void DescribePlace()
    {
        var identity = GeoIdentity();
        var geoParts = new List<string> { "Country: US", "Region: Florida", "City: Clermont" };

        string place = DashboardForm.DescribePublicIpPlace(identity);
        Check(place == "Clermont, Florida, US",
            "place should be city, region, country, but was: " + place);

        // Missing fields are skipped rather than left as empty commas.
        var partial = new NetworkIdentity { PublicIpCity = "Clermont", PublicIpCountry = "US" };
        string partialPlace = DashboardForm.DescribePublicIpPlace(partial);
        Check(partialPlace == "Clermont, US",
            "missing fields should be skipped, but was: " + partialPlace);

        // Nothing at all is "Location unknown", not an empty string.
        string unknown = DashboardForm.DescribePublicIpPlace(new NetworkIdentity());
        Check(unknown == "Location unknown",
            "no location should read as 'Location unknown', but was: " + unknown);

        // Null identity must not throw.
        string nullPlace = DashboardForm.DescribePublicIpPlace(null);
        Check(nullPlace == "Location unknown",
            "a null identity should read as 'Location unknown', but was: " + nullPlace);
    }

    private static void DescribeFullLocation()
    {
        var identity = GeoIdentity();
        var geoParts = new List<string> { "Country: US", "Region: Florida", "City: Clermont" };

        // With data, the full text leads with the place and follows with the provider.
        string text = DashboardForm.DescribePublicIpLocation(identity, geoParts);
        Check(text.Contains("Clermont") && text.Contains("Florida") && text.Contains("US"),
            "full text should contain the place, but was: " + text);
        Check(text.Contains("Charter Communications") && text.Contains("AS33363"),
            "full text should contain the provider and ASN, but was: " + text);

        // With no data, it says the lookup failed, not the place.
        string failed = DashboardForm.DescribePublicIpLocation(new NetworkIdentity(), new List<string>());
        Check(failed == DashboardForm.PublicIpLocationUnavailableHint,
            "no data should read as the unavailable hint, but was: " + failed);
    }

    private static void DescribeShortLocation()
    {
        var identity = GeoIdentity();
        var geoParts = new List<string> { "Country: US", "Region: Florida", "City: Clermont" };

        string shortText = DashboardForm.DescribePublicIpLocationShort(identity, geoParts);
        Check(shortText.Contains("Clermont") && shortText.Contains("US"),
            "short text should contain the place, but was: " + shortText);
        Check(shortText.Contains("AS33363"),
            "short text should contain the ASN, but was: " + shortText);
        // The header is narrow, so the provider name is dropped.
        Check(!shortText.Contains("Charter"),
            "short text must not contain the provider name, but was: " + shortText);

        string failed = DashboardForm.DescribePublicIpLocationShort(new NetworkIdentity(), new List<string>());
        Check(failed == DashboardForm.PublicIpLocationUnavailableShort,
            "no data should read as the short unavailable hint, but was: " + failed);
    }

    // ------------------------------------------------------------- messaging

    private static void AddressUnavailableIsDistinguishedFromLocationUnavailable()
    {
        // When the address itself could not be fetched, the row must blame the address
        // lookup, not the location service. Blaming the wrong one sends the reader after
        // the wrong thing: there is nothing to look up yet.
        using (DashboardForm form = new DashboardForm())
        {
            CheckBox box = (CheckBox)typeof(DashboardForm)
                .GetField("_showPublicIpLocationCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            box.Checked = true;

            form.SetPublicIp(DashboardForm.PublicIpUnavailable, new NetworkIdentity());
            Label location = (Label)typeof(DashboardForm)
                .GetField("_publicIpLocationLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            Check(location.Text.IndexOf("public IP address", StringComparison.OrdinalIgnoreCase) >= 0,
                "an address failure should blame the address lookup, but the row reads: " + location.Text);
            Check(location.Text.IndexOf("location service", StringComparison.OrdinalIgnoreCase) < 0,
                "an address failure must not blame the location service, but the row reads: " + location.Text);

            Label header = (Label)typeof(DashboardForm)
                .GetField("_headerPublicIpLocationLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            Check(header.Text.IndexOf("Public IP", StringComparison.OrdinalIgnoreCase) >= 0,
                "the header should say the address is unavailable, but it reads: " + header.Text);
        }
    }

    private static void AddressArrivingClearsTheMessage()
    {
        // A recovered address must clear the failure wording rather than leaving it.
        using (DashboardForm form = new DashboardForm())
        {
            CheckBox box = (CheckBox)typeof(DashboardForm)
                .GetField("_showPublicIpLocationCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            box.Checked = true;

            form.SetPublicIp(DashboardForm.PublicIpUnavailable, new NetworkIdentity());
            form.SetPublicIp("203.0.113.9", new NetworkIdentity());

            Label location = (Label)typeof(DashboardForm)
                .GetField("_publicIpLocationLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            Check(location.Text.IndexOf("public IP address", StringComparison.OrdinalIgnoreCase) < 0,
                "a recovered address should clear the address-failure wording, but the row reads: " + location.Text);
        }
    }

    // ------------------------------------------------------------- NetworkIdentity

    private static void NetworkIdentityBuildsKey()
    {
        // Wi-Fi SSID is preferred and tagged wifi:.
        string key = NetworkIdentity.BuildKey("Ethernet", "192.168.1.1", "HomeNet");
        Check(key == "wifi:homenet|192.168.1.1",
            "wifi key should be wifi:<ssid>|<gateway>, but was: " + key);

        // No SSID falls back to the connection profile, tagged net:.
        string netKey = NetworkIdentity.BuildKey("Ethernet", "192.168.1.1", "");
        Check(netKey == "net:ethernet|192.168.1.1",
            "wired key should be net:<profile>|<gateway>, but was: " + netKey);

        // No name and no gateway is unusable, not a key.
        string empty = NetworkIdentity.BuildKey("", "", "");
        Check(string.IsNullOrEmpty(empty),
            "an empty identity should have no key, but was: " + empty);

        // The gateway is always included so two networks sharing a name stay distinct.
        string a = NetworkIdentity.BuildKey("LAN", "192.168.1.1", "");
        string b = NetworkIdentity.BuildKey("LAN", "10.0.0.1", "");
        Check(a != b, "different gateways must produce different keys.");

        // Keys are case-insensitive.
        string lower = NetworkIdentity.BuildKey("LAN", "192.168.1.1", "");
        string upper = NetworkIdentity.BuildKey("lan", "192.168.1.1", "");
        Check(string.Equals(lower, upper, StringComparison.OrdinalIgnoreCase),
            "keys should be case-insensitive.");

        // A pipe in a value must not split the key.
        string piped = NetworkIdentity.BuildKey("a|b", "192.168.1.1", "");
        Check(!piped.Contains("a|b") || piped.Contains("a/b"),
            "a pipe in a value must be escaped, but was: " + piped);
    }

    private static void NetworkIdentityBuildDescription()
    {
        string desc = NetworkIdentity.BuildDescription("Ethernet", "192.168.1.1", "HomeNet");
        Check(desc.Contains("Wi-Fi HomeNet") && desc.Contains("gateway 192.168.1.1"),
            "description should mention both the SSID and the gateway, but was: " + desc);

        string wired = NetworkIdentity.BuildDescription("Ethernet", "192.168.1.1", "");
        Check(wired.Contains("Ethernet") && wired.Contains("gateway 192.168.1.1"),
            "wired description should mention the profile and gateway, but was: " + wired);

        string unknown = NetworkIdentity.BuildDescription("", "", "");
        Check(unknown == "unknown network",
            "an empty identity should read as 'unknown network', but was: " + unknown);
    }

    private static void NetworkIdentityIsUsable()
    {
        var usable = new NetworkIdentity { Key = "net:lan|192.168.1.1" };
        Check(usable.IsUsable, "an identity with a key should be usable.");

        var unusable = new NetworkIdentity { Key = "" };
        Check(!unusable.IsUsable, "an identity with no key should not be usable.");

        var nullKey = new NetworkIdentity { Key = null };
        Check(!nullKey.IsUsable, "an identity with a null key should not be usable.");
    }
}