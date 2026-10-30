using System;
using System.Collections.Generic;
using MacRando;

/// <summary>
/// OUI vendor lookup.
///
/// Read-only and offline: the database is bundled, so the lookup works on an air-gapped
/// machine and needs no consent. The rules that matter: a well-formed MAC returns its
/// registered vendor, an unknown or malformed address returns "Unknown" rather than
/// throwing, and the parser ignores comments, blanks, and malformed lines.
/// </summary>
internal static class OuiTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            LookupKnownPrefix();
            LookupUnknownPrefix();
            LookupMalformed();
            LookupHandlesSeparators();
            ParseIgnoresCommentsAndBlanks();
            ParseFirstEntryWins();
            ParseIgnoresMalformedLines();
            Console.WriteLine("oui-tests=OK;checks=" + _checks);
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

    private static readonly Dictionary<string, string> SampleDatabase = OuiLookup.Parse(
        "001B21\tIntel\n" +
        "000C29\tVMware\n" +
        "0017F2\tApple\n" +
        "020000\tLocally administered (randomized)\n");

    private static void LookupKnownPrefix()
    {
        Check(OuiLookup.Lookup("00:1B:21:00:00:01", SampleDatabase) == "Intel",
            "a known Intel prefix should resolve to Intel.");
        Check(OuiLookup.Lookup("00-0C-29-AB-CD-EF", SampleDatabase) == "VMware",
            "a known VMware prefix should resolve to VMware.");
        Check(OuiLookup.Lookup("0017F2AABBCC", SampleDatabase) == "Apple",
            "a known Apple prefix without separators should resolve to Apple.");
    }

    private static void LookupUnknownPrefix()
    {
        Check(OuiLookup.Lookup("FF:FF:FF:00:00:01", SampleDatabase) == "Unknown",
            "an unregistered prefix should report Unknown.");
        Check(OuiLookup.Lookup("DE:AD:BE:EF:00:01", SampleDatabase) == "Unknown",
            "an unregistered prefix should report Unknown.");
    }

    private static void LookupMalformed()
    {
        Check(OuiLookup.Lookup(null, SampleDatabase) == "Unknown", "null should report Unknown.");
        Check(OuiLookup.Lookup("", SampleDatabase) == "Unknown", "empty should report Unknown.");
        Check(OuiLookup.Lookup("   ", SampleDatabase) == "Unknown", "whitespace should report Unknown.");
        Check(OuiLookup.Lookup("00:1B", SampleDatabase) == "Unknown", "too short should report Unknown.");
        Check(OuiLookup.Lookup("GG:HH:II:00:00:01", SampleDatabase) == "Unknown", "non-hex should report Unknown.");
        Check(OuiLookup.Lookup("not a mac", SampleDatabase) == "Unknown", "garbage should report Unknown.");
    }

    private static void LookupHandlesSeparators()
    {
        Check(OuiLookup.Lookup("00:1B:21:00:00:01", SampleDatabase) == "Intel", "colon form should work.");
        Check(OuiLookup.Lookup("00-1B-21-00-00-01", SampleDatabase) == "Intel", "hyphen form should work.");
        Check(OuiLookup.Lookup("001B.2100.0001", SampleDatabase) == "Intel", "dot form should work.");
    }

    private static void ParseIgnoresCommentsAndBlanks()
    {
        string text = "# comment\n\n001B21\tIntel\n   \n# another\n";
        var map = OuiLookup.Parse(text);
        Check(map.Count == 1, "comments and blanks should be ignored, but got " + map.Count + " entries.");
        Check(map["001B21"] == "Intel", "the single entry should be Intel.");
    }

    private static void ParseFirstEntryWins()
    {
        string text = "001B21\tIntel\n001B21\tSomeone Else\n";
        var map = OuiLookup.Parse(text);
        Check(map["001B21"] == "Intel",
            "the first entry should win, but was: " + map["001B21"]);
    }

    private static void ParseIgnoresMalformedLines()
    {
        string text = "short\tIntel\n001B21\t\nGGGGGG\tIntel\n001B21\tIntel\n";
        var map = OuiLookup.Parse(text);
        Check(map.Count == 1, "malformed lines should be ignored, but got " + map.Count + " entries.");
    }
}