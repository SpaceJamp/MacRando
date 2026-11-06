using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Guards the LICENSE file against being edited by accident.
///
/// A stray edit to a licence is a legal problem, not a build failure, and it is invisible
/// in a diff of a wall of legal text unless someone reads it closely. So the sections and
/// the clauses that carry real obligations must all be present, and the copyright, which
/// is stated in several places that are edited separately, must agree.
/// </summary>
internal static class LicenseTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            EverySectionIsPresent();
            TheKeyTermsAreIntact();
            TheNoticesAgreeWithEachOther();
            TheFullTextIsShipped();
            Console.WriteLine("license-tests=OK;checks=" + _checks);
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

    private static string RepositoryRoot()
    {
        return Environment.GetEnvironmentVariable("MACRANDO_REPO_ROOT");
    }

    private static string ReadFile(string relative)
    {
        string root = RepositoryRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    private static void EverySectionIsPresent()
    {
        string license = ReadFile("LICENSE");
        if (license == null)
        {
            return;
        }

        string[] required =
        {
            "# Personal and Non-Commercial Use License (PNC-1.2)",
            "## 1. Definitions and Interpretation",
            "## 2. License Grant",
            "## 3. Noncommercial Restrictions",
            "## 4. Source Code and Modified Versions",
            "## 5. Attribution",
            "## 6. Names, Trademarks, and Endorsement",
            "## 7. Contributions to the Author",
            "## 8. Termination",
            "## 9. Remedies",
            "## 10. Indemnification",
            "## 11. Disclaimer of Warranty",
            "## 12. Limitation of Liability",
            "## 13. Ownership and No Implied Rights",
            "## 14. Changes to This License",
            "## 15. Rights Under Applicable Law",
            "## 16. Notices",
            "## 17. General Terms",
            "## 18. Acceptance"
        };
        foreach (string heading in required)
        {
            Check(license.IndexOf(heading, StringComparison.Ordinal) >= 0,
                "The LICENSE file is missing '" + heading + "'. The terms must be the complete " +
                "PNC-1.2 text, not a summary or a partial copy.");
        }
    }

    /// <summary>
    /// Spot-checks the clauses a careless edit would most plausibly change, and whose loss
    /// would alter what a recipient is agreeing to.
    /// </summary>
    private static void TheKeyTermsAreIntact()
    {
        string license = ReadFile("LICENSE");
        if (license == null)
        {
            return;
        }

        string[] clauses =
        {
            "Use the Software for any Commercial Use.",
            "Any Commercial Use requires a separate written license from the Author.",
            "Make the complete Source Code of the Modified Version available to every recipient",
            "License the Modified Version under this same license",
            "Based on software by Jeffrey Horne, licensed under PNC-1.2.",
            "THE SOFTWARE IS PROVIDED \"AS IS\" AND \"AS AVAILABLE\", WITHOUT WARRANTY OF ANY KIND",
            "IN NO EVENT SHALL THE AUTHOR BE LIABLE"
        };
        foreach (string clause in clauses)
        {
            Check(license.IndexOf(clause, StringComparison.Ordinal) >= 0,
                "The LICENSE file is missing or has altered this clause: '" + clause + "'.");
        }

        Check(license.IndexOf('[') < 0, "The LICENSE file still has an unfilled [placeholder].");
    }

    /// <summary>
    /// The copyright is stated in several places, and they are edited separately. A
    /// mismatch makes a licence look careless, and the year should not drift between them.
    /// </summary>
    private static void TheNoticesAgreeWithEachOther()
    {
        string license = ReadFile("LICENSE");
        string readme = ReadFile("README.md");
        if (license == null || readme == null)
        {
            return;
        }

        Match inLicense = Regex.Match(license, @"(?m)^Copyright \(c\) (\d{4}) (.+?)\s*$");
        Check(inLicense.Success, "The LICENSE file has no 'Copyright (c) <year> <holder>' line.");
        string notice = "Copyright (c) " + inLicense.Groups[1].Value + " " + inLicense.Groups[2].Value;

        Check(MacRando.LicenseInfo.Copyright == notice,
            "The copyright differs: LICENSE says '" + notice + "' and the application says '" +
            MacRando.LicenseInfo.Copyright + "'");
        Check(readme.IndexOf(notice, StringComparison.Ordinal) >= 0,
            "README.md does not carry the LICENSE copyright line: '" + notice + "'");
        Check(readme.IndexOf(MacRando.LicenseInfo.ShortName, StringComparison.Ordinal) >= 0,
            "README.md does not name the license " + MacRando.LicenseInfo.ShortName);
    }

    /// <summary>
    /// Section 5 requires a complete copy of the licence with any distribution, so it has
    /// to travel with the software. Checked in the three places it must appear.
    /// </summary>
    private static void TheFullTextIsShipped()
    {
        string installer = ReadFile("installer.iss");
        string package = ReadFile("package.ps1");
        string build = ReadFile("build.ps1");
        if (RepositoryRoot() == null)
        {
            return;
        }
        Check(installer != null && package != null && build != null,
            "installer.iss, package.ps1 and build.ps1 must all exist to check the licence ships.");

        Check(Regex.IsMatch(installer, @"(?m)^Source:\s*""LICENSE"""),
            "installer.iss no longer installs the LICENSE file. Section 5 of the licence " +
            "requires a complete copy with any distribution.");
        Check(package.IndexOf("'LICENSE'", StringComparison.Ordinal) >= 0,
            "package.ps1 no longer includes LICENSE in the release archive, so a user who " +
            "downloads the ZIP would not receive the licence text.");
        Check(build.IndexOf("MacRando.LICENSE.txt", StringComparison.Ordinal) >= 0,
            "build.ps1 no longer embeds the licence as a resource, so the application could not " +
            "show the full text when the LICENSE file is not beside the executable.");
    }
}
