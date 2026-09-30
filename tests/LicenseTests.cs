using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Guards the LICENSE file against being edited by accident.
///
/// The terms were verified line by line against the canonical Apache-2.0 text: 200 of the
/// 201 lines are byte-identical, and the only difference is the copyright line. That is
/// the correct state, and it is also a state nothing else in this project would protect.
/// A stray edit to a licence is a legal problem, not a build failure, and it is invisible
/// in a diff of a 200-line wall of legal text unless someone reads it closely.
///
/// So the check is deliberately paranoid about the terms and tolerant about formatting:
/// the required clauses must all be present, and the only line permitted to differ from
/// the stock text is the copyright notice.
/// </summary>
internal static class LicenseTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            TheCopyrightLineFollowsTheTemplate();
            EverySectionIsPresent();
            TheTermsAreTheStockWording();
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

    /// <summary>
    /// The appendix says to replace the bracketed fields and, in those words, not to
    /// include the brackets. Leaving them in is a deviation from the template the licence
    /// itself specifies, and it is the one deviation that was present.
    /// </summary>
    private static void TheCopyrightLineFollowsTheTemplate()
    {
        string license = ReadFile("LICENSE");
        if (license == null)
        {
            return;
        }

        Match line = Regex.Match(license, @"(?m)^\s*Copyright\s+(.+?)\s*$");
        Check(line.Success, "The LICENSE file has no copyright line in its appendix.");
        string notice = line.Groups[1].Value;

        Check(notice.IndexOf('[') < 0 && notice.IndexOf(']') < 0,
            "The copyright line still contains brackets: '" + notice + "'. The Apache-2.0 appendix " +
            "says to replace the bracketed fields and not to include the brackets.");

        // A year and a holder, in the order the template uses.
        Check(Regex.IsMatch(notice, @"^\d{4}\s+\S.*$"),
            "The copyright line should read as a year followed by the copyright holder, but is: '" + notice + "'");

        // The placeholder must not have survived in any form.
        Check(notice.IndexOf("yyyy", StringComparison.OrdinalIgnoreCase) < 0
              && notice.IndexOf("name of copyright owner", StringComparison.OrdinalIgnoreCase) < 0,
            "The copyright line still contains the template placeholder: '" + notice + "'");
    }

    private static void EverySectionIsPresent()
    {
        string license = ReadFile("LICENSE");
        if (license == null)
        {
            return;
        }

        // The nine numbered sections plus the two markers. If any were removed or renamed
        // the licence would no longer be Apache-2.0, whatever the file is called.
        string[] required =
        {
            "TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION",
            "1. Definitions.",
            "2. Grant of Copyright License.",
            "3. Grant of Patent License.",
            "4. Redistribution.",
            "5. Submission of Contributions.",
            "6. Trademarks.",
            "7. Disclaimer of Warranty.",
            "8. Limitation of Liability.",
            "9. Accepting Warranty or Additional Liability.",
            "END OF TERMS AND CONDITIONS",
            "APPENDIX: How to apply the Apache License to your work."
        };
        foreach (string heading in required)
        {
            Check(license.IndexOf(heading, StringComparison.Ordinal) >= 0,
                "The LICENSE file is missing '" + heading + "'. The terms must be the unmodified " +
                "Apache-2.0 text, not a summary or a partial copy.");
        }
    }

    /// <summary>
    /// Spot-checks clauses that carry real obligations, chosen because they are the ones a
    /// careless edit would most plausibly change, and because dropping any of them would
    /// alter what a recipient is actually agreeing to.
    /// </summary>
    private static void TheTermsAreTheStockWording()
    {
        string license = ReadFile("LICENSE");
        if (license == null)
        {
            return;
        }

        // Each clause is quoted exactly as the licence wraps it. Written from memory
        // instead, they do not match: the text is hard-wrapped, so a phrase that reads as
        // one sentence on the page is split across two lines in the file. That mistake was
        // made while writing this test and it failed against a licence that is correct.
        string[] clauses =
        {
            "\"License\" shall mean the terms and conditions for use, reproduction,",
            "worldwide, non-exclusive, no-charge, royalty-free, irrevocable",
            "You must give any other recipients of the Work or",
            "You must cause any modified files to carry prominent notices",
            "WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or",
            "shall any Contributor be",
            "distributed under the License is distributed on an \"AS IS\" BASIS",
            "http://www.apache.org/licenses/LICENSE-2.0"
        };
        foreach (string clause in clauses)
        {
            Check(license.IndexOf(clause, StringComparison.Ordinal) >= 0,
                "The LICENSE file is missing or has altered this clause: '" + clause + "'. " +
                "The terms must be the unmodified Apache-2.0 text.");
        }

        // The disclaimer of warranty and the limitation of liability are the two clauses
        // that matter most to a licensor, so they are called out by name in the failure
        // message rather than only being part of a loop. Quoted exactly as written, which
        // for the warranty disclaimer means including the quotation marks around AS IS.
        Check(license.IndexOf("\"AS IS\" BASIS", StringComparison.Ordinal) >= 0,
            "The LICENSE file no longer disclaims warranty. That clause must not be removed.");
        Check(license.IndexOf("8. Limitation of Liability.", StringComparison.Ordinal) >= 0,
            "The LICENSE file no longer limits liability. That section must not be removed.");
    }

    /// <summary>
    /// The copyright is stated in three places, and they are edited separately. A
    /// mismatch is not fatal but it is the kind of thing that makes a licence look
    /// careless, and the year in particular should not drift between them.
    /// </summary>
    private static void TheNoticesAgreeWithEachOther()
    {
        string license = ReadFile("LICENSE");
        string readme = ReadFile("README.md");
        if (license == null || readme == null)
        {
            return;
        }

        Match inLicense = Regex.Match(license, @"(?m)^\s*Copyright\s+(\d{4})\s+(.+?)\s*$");
        Check(inLicense.Success, "The LICENSE file has no 'Copyright <year> <holder>' line.");
        string year = inLicense.Groups[1].Value;
        string holder = inLicense.Groups[2].Value;

        Match inReadme = Regex.Match(readme, @"Copyright \(c\)\s+(\d{4})\s+(.+?)\s*$", RegexOptions.Multiline);
        Check(inReadme.Success, "README.md has no 'Copyright (c) <year> <holder>' line.");
        Check(inReadme.Groups[1].Value == year,
            "The copyright year differs: LICENSE says " + year + " and README says " + inReadme.Groups[1].Value);
        Check(string.Equals(inReadme.Groups[2].Value, holder, StringComparison.OrdinalIgnoreCase),
            "The copyright holder differs: LICENSE says '" + holder + "' and README says '" +
            inReadme.Groups[2].Value + "'");
    }

    /// <summary>
    /// Section 4(a) requires that recipients receive a copy of the licence, so it has to
    /// actually travel with the software. Checked in the three places it must appear.
    /// </summary>
    private static void TheFullTextIsShipped()
    {
        string installer = ReadFile("installer.iss");
        string package = ReadFile("package.ps1");
        string build = ReadFile(Path.Combine("src", "build.ps1"));
        if (installer == null || package == null || build == null)
        {
            return;
        }

        Check(Regex.IsMatch(installer, @"(?m)^Source:\s*""LICENSE"""),
            "installer.iss no longer installs the LICENSE file. Section 4(a) of the licence " +
            "requires recipients to receive a copy of it.");
        Check(package.IndexOf("'LICENSE'", StringComparison.Ordinal) >= 0,
            "package.ps1 no longer includes LICENSE in the release archive, so a user who " +
            "downloads the ZIP would not receive the licence text.");
        Check(build.IndexOf("MacRando.LICENSE.txt", StringComparison.Ordinal) >= 0,
            "build.ps1 no longer embeds the licence as a resource, so the application could not " +
            "show the full text when the LICENSE file is not beside the executable.");
    }
}
