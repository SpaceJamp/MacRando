using System;
using System.Collections.Generic;
using DeviceTrackingService = MacRando.DeviceTrackingService;
using GdidKind = MacRando.GdidKind;

/// <summary>
/// Coverage for the read-only device tracking report.
///
/// The analysis is a pure function, so the whole thing is testable against machines that do
/// not exist. That matters here more than usual: the first version of this report asserted
/// a Microsoft-account state from the absence of a registry cache, in a sentence that the
/// very next line contradicted. Conclusions like that are only trustworthy if they are
/// pinned by a test.
/// </summary>
internal static class DeviceTrackingTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            LidClassification();
            PrefixedClassification();
            UnrecognizedValuesAreNotTreatedAsValid();
            MaskingHidesTheIdentifier();
            TelemetryIsOffOnlyWhereTheEditionHonoursIt();
            TelemetryPolicyBeatsTheSettingsInterface();
            TelemetryOnHomeIsStillNotOff();
            TelemetryOnOtherLevels();
            UnconfiguredTelemetryIsNotClaimedAsOff();
            AStoredIdentifierIsReportedAsPresent();
            NoIdentifierIsNotReportedAsPresent();
            TheReportNeverClaimsTheAccountIsUntracked();
            TheReportAlwaysSaysItChangedNothing();
            TheMachineGuidIsFlaggedAsDoNotTouch();
            SignedInIdentitiesAreCountedNotListed();
            BundleLineForState();
            Console.WriteLine("device-tracking-tests=OK;checks=" + _checks);
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

    private static MacRando.DeviceTrackingSnapshot Snapshot()
    {
        return new MacRando.DeviceTrackingSnapshot { Unavailable = new List<string>() };
    }

    private static MacRando.DeviceTrackingReport Analyze(MacRando.DeviceTrackingSnapshot snapshot)
    {
        return new DeviceTrackingService().Analyze(snapshot);
    }

    private static string AllText(MacRando.DeviceTrackingReport report)
    {
        return report.ToDisplayText();
    }

    private static void LidClassification()
    {
        Check(GdidKind.Classify("0011223344556677") == GdidKind.LocalLid,
            "a 16 character hex value is the local LID form");
        Check(GdidKind.Classify("00112233445566AB") == GdidKind.LocalLid,
            "hex classification must accept upper case");
        Check(GdidKind.Classify("  0011223344556677  ") == GdidKind.LocalLid,
            "surrounding whitespace must not change the classification");
        Check(GdidKind.Describe(GdidKind.LocalLid).Contains("local LID form"),
            "the description should name the form");
    }

    private static void PrefixedClassification()
    {
        Check(GdidKind.Classify("g:6755467234350028") == GdidKind.GlobalPrefixed,
            "the g: prefixed form is a global device ID");
        Check(GdidKind.Classify("G:6755467234350028") == GdidKind.GlobalPrefixed,
            "the g: prefix is matched case-insensitively");
        Check(GdidKind.Classify("g:") == GdidKind.Unrecognized,
            "a g: prefix with no payload is not a device ID");
        Check(GdidKind.Classify("g:675546723435002") == GdidKind.Unrecognized,
            "a g: payload of the wrong length is not a device ID");
    }

    private static void UnrecognizedValuesAreNotTreatedAsValid()
    {
        // The failure that matters: something present but unfamiliar must not be reported
        // as a known identifier, or the report invites someone to act on a wrong reading.
        Check(GdidKind.Classify("not-a-guid") == GdidKind.Unrecognized, "free text is not an identifier");
        Check(GdidKind.Classify("001122334455667") == GdidKind.Unrecognized, "15 hex characters is not the LID form");
        Check(GdidKind.Classify("001122334455667788") == GdidKind.Unrecognized, "18 hex characters is not the LID form");
        Check(GdidKind.Classify("00112233445566zz") == GdidKind.Unrecognized, "non-hex is not an identifier");
        Check(GdidKind.Classify(null) == GdidKind.None, "null is nothing present");
        Check(GdidKind.Classify("   ") == GdidKind.None, "whitespace is nothing present");
        Check(GdidKind.Classify("") == GdidKind.None, "empty is nothing present");
    }

    private static void MaskingHidesTheIdentifier()
    {
        string masked = DeviceTrackingService.Mask("0011223344556677");
        Check(!masked.Contains("0011223344556677"), "the raw identifier must never appear in a masked value");
        Check(masked.Contains("001") && masked.Contains("677"), "a masked value should keep enough to recognize");
        Check(masked.Contains("16"), "a masked value should state the length");
        Check(DeviceTrackingService.Mask(null) == "(none)", "a missing identifier reads as none");
        Check(DeviceTrackingService.Mask("") == "(none)", "an empty identifier reads as none");
        Check(DeviceTrackingService.Mask("abc") == "***", "a short identifier is fully masked");

        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.Lid = "0011223344556677";
        MacRando.DeviceTrackingReport report = Analyze(snapshot);
        Check(!AllText(report).Contains("0011223344556677"),
            "the rendered report must not contain the identifier in clear text");
    }

    private static void TelemetryIsOffOnlyWhereTheEditionHonoursIt()
    {
        // The whole point of the edition lookup. On Pro, 0 is not off.
        MacRando.DeviceTrackingSnapshot enterprise = Snapshot();
        enterprise.EditionId = "Enterprise";
        enterprise.AllowTelemetryPolicy = 0;
        string description;
        bool fullyOff;
        DeviceTrackingService.InterpretTelemetry(enterprise, out description, out fullyOff);
        Check(fullyOff, "an Enterprise machine honouring 0 is diagnostic data off");
        Check(description.Contains("honours it"), "the description should say the setting is honoured");

        MacRando.DeviceTrackingSnapshot server = Snapshot();
        server.EditionId = "ServerStandard";
        server.AllowTelemetryPolicy = 0;
        DeviceTrackingService.InterpretTelemetry(server, out description, out fullyOff);
        Check(fullyOff, "a Server machine honouring 0 is diagnostic data off");

        MacRando.DeviceTrackingSnapshot education = Snapshot();
        education.EditionId = "ProfessionalEducation";
        education.AllowTelemetryPolicy = 0;
        DeviceTrackingService.InterpretTelemetry(education, out description, out fullyOff);
        Check(fullyOff, "an Education machine honouring 0 is diagnostic data off");
    }

    private static void TelemetryOnHomeIsStillNotOff()
    {
        string description;
        bool fullyOff;
        foreach (string edition in new[] { "Professional", "Home", "Core", "" })
        {
            MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
            snapshot.EditionId = edition;
            snapshot.AllowTelemetryPolicy = 0;
            DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
            Check(!fullyOff, "edition '" + edition + "' must not be reported as fully off");
            Check(description.Contains("Required") || description.Contains("required"),
                "edition '" + edition + "' should say the required floor still applies");
        }
    }

    private static void TelemetryPolicyBeatsTheSettingsInterface()
    {
        // Both locations are read, and the policy one is what Windows actually honours.
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.EditionId = "Enterprise";
        snapshot.AllowTelemetryPolicy = 0;
        snapshot.AllowTelemetryCurrentVersion = 3;
        string description;
        bool fullyOff;
        DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
        Check(fullyOff, "the policy value should win over the Settings interface value");

        string text = AllText(Analyze(snapshot));
        Check(text.Contains("takes precedence"), "the report should say which location won");
    }

    private static void TelemetryOnOtherLevels()
    {
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.AllowTelemetryPolicy = 3;
        string description;
        bool fullyOff;
        DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
        Check(!fullyOff, "optional diagnostic data is not off");
        Check(description.Contains("3"), "the description should state the level");

        snapshot.AllowTelemetryPolicy = 1;
        DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
        Check(!fullyOff && description.Contains("required"), "level 1 is required data, not off");

        snapshot.AllowTelemetryPolicy = 9;
        DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
        Check(!fullyOff && description.Contains("not a documented"), "an unknown level should be called out as such");
    }

    private static void UnconfiguredTelemetryIsNotClaimedAsOff()
    {
        string description;
        bool fullyOff;
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.EditionId = "Enterprise";
        DeviceTrackingService.InterpretTelemetry(snapshot, out description, out fullyOff);
        Check(!fullyOff, "an unconfigured setting must not be reported as off");
        Check(description.Contains("not configured"), "the report should say nothing is configured");

        MacRando.DeviceTrackingReport report = Analyze(snapshot);
        Check(!report.TelemetryFullyOff, "the report must not claim diagnostic data is off");
        Check(AllText(report).Contains("Windows default"), "the report should point at the default");
    }

    private static void AStoredIdentifierIsReportedAsPresent()
    {
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.Lid = "0011223344556677";
        MacRando.DeviceTrackingReport report = Analyze(snapshot);
        Check(report.LidShape == GdidKind.LocalLid, "the shape should be recorded on the report");
        Check(report.Headline.Contains("is stored on this machine"), "the headline should say it is present");
        Check(!report.TelemetryFullyOff, "an unrelated setting must not be asserted here");
    }

    private static void NoIdentifierIsNotReportedAsPresent()
    {
        MacRando.DeviceTrackingReport report = Analyze(Snapshot());
        Check(report.LidShape == GdidKind.None, "a missing value is none");
        Check(report.Headline.Contains("No Global Device ID"), "the headline should say it is absent");
        Check(AllText(report).Contains("not present"), "the report should state the absence plainly");
    }

    private static void TheReportNeverClaimsTheAccountIsUntracked()
    {
        // The regression: with no g: cache but a signed-in identity, the report used to say
        // the absence was "consistent with not being signed in with a Microsoft account",
        // one line before reporting that an account is signed in.
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.Lid = "0011223344556677";
        snapshot.IdentityEntryCount = 1;
        string text = AllText(Analyze(snapshot));
        Check(!text.Contains("consistent with not being signed in"),
            "the report must not infer account state from the absence of a cache");
        Check(text.Contains("is 1 Microsoft account identity entry"),
            "the report should still state the signed-in identity");
        Check(text.Contains("not evidence that the machine is untracked"),
            "the report should be explicit that absence is not proof");
    }

    private static void TheReportAlwaysSaysItChangedNothing()
    {
        MacRando.DeviceTrackingSnapshot present = Snapshot();
        present.Lid = "0011223344556677";
        foreach (MacRando.DeviceTrackingSnapshot snapshot in new[] { Snapshot(), present })
        {
            Check(AllText(Analyze(snapshot)).Contains("No registry value, file, or setting was changed"),
                "a read-only report must say so on its face");
        }
    }

    private static void TheMachineGuidIsFlaggedAsDoNotTouch()
    {
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.MachineGuid = "97e2816b-8de6-42d5-9458-b35209ddb17a";
        string text = AllText(Analyze(snapshot));
        Check(text.Contains("leave alone") || text.Contains("worth leaving alone"),
            "the machine GUID should be flagged as something not to change");
        Check(text.Contains("activation"), "the reason should name the real cost");
        Check(!text.Contains("97e2816b-8de6-42d5-9458-b35209ddb17a"),
            "the machine GUID must be masked in the report");
    }

    private static void SignedInIdentitiesAreCountedNotListed()
    {
        // The report must not write the account name into a file that gets attached to a
        // bug report. A count answers the diagnostic question without that.
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.IdentityEntryCount = 2;
        string text = AllText(Analyze(snapshot));
        Check(text.Contains("are 2 Microsoft account identity entries"), "multiple identities should be pluralized");
        Check(!text.Contains("@"), "no account address should appear in the report");
    }

    private static void BundleLineForState()
    {
        MacRando.DeviceTrackingSnapshot snapshot = Snapshot();
        snapshot.Unavailable = new List<string> { "Identity negative cache: access denied" };
        string text = AllText(Analyze(snapshot));
        Check(text.Contains("Could not be read:"), "unreadable sources should be listed");
        Check(text.Contains("access denied"), "the reason should be carried through");
    }
}
