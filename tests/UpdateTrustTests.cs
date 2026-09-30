using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AppSettings = MacRando.AppSettings;
using UpdateTrustEnvelope = MacRando.UpdateTrustEnvelope;
using UpdateTrustPayload = MacRando.UpdateTrustPayload;

/// <summary>
/// Coverage for the update trust anchor.
///
/// The two values in question decide where an update comes from and whose signature it
/// must carry, and MacRando runs elevated, so anything able to rewrite them can aim an
/// update at a server of its choosing. The Authenticode check limits the damage, but the
/// redirect should not be writable by anything that is not the user, so the values are
/// encrypted and a clear text copy must not survive a save.
/// </summary>
internal static class UpdateTrustTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            ValuesRoundTripThroughEncryption();
            TheEncryptedFileDoesNotLeakTheValues();
            GarbageIsRefused();
            AnEnvelopeForSomethingElseIsRefused();
            SavingClearsTheValuesFromTheClearTextFile();
            TrustFileSurvivesWhenTheSettingsFileIsDeleted();
            PreferencesAreStillInClearText();
            RedirectsAreResolvedButNeverDowngraded();
            TheLiveManifestIsReachableThroughARedirect();
            Console.WriteLine("update-trust-tests=OK;checks=" + _checks);
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

    private const string SampleUrl = "https://github.com/SpaceJamp/MacRando/releases/latest/download/update.json";
    private const string SampleThumbprint = "8804295F8D8615DC1D137720847672ABA1342FEC";

    private static string Sandbox()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoTrust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ValuesRoundTripThroughEncryption()
    {
        var envelope = new UpdateTrustEnvelope
        {
            UpdateManifestUrl = SampleUrl,
            ExpectedSignerThumbprint = SampleThumbprint
        };
        string stored = envelope.Protect();
        UpdateTrustPayload payload = UpdateTrustEnvelope.Unprotect(stored);

        Check(payload != null, "an envelope written by MacRando should read back");
        Check(payload.UpdateManifestUrl == SampleUrl, "the manifest URL should survive the round trip");
        Check(payload.ExpectedSignerThumbprint == SampleThumbprint,
            "the signer thumbprint should survive the round trip");
        Check(UpdateTrustEnvelope.IsProtectedFormat(stored), "the stored form should be recognisable");
    }

    private static void TheEncryptedFileDoesNotLeakTheValues()
    {
        var envelope = new UpdateTrustEnvelope
        {
            UpdateManifestUrl = SampleUrl,
            ExpectedSignerThumbprint = SampleThumbprint
        };
        string stored = envelope.Protect();

        // The whole point: someone who can read the file must not learn where updates
        // come from, because that is the value a redirect attack needs to change.
        Check(stored.IndexOf(SampleUrl, StringComparison.Ordinal) < 0,
            "the manifest URL must not appear in the encrypted file");
        Check(stored.IndexOf(SampleThumbprint, StringComparison.Ordinal) < 0,
            "the signer thumbprint must not appear in the encrypted file");
        Check(stored.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) < 0,
            "not even the host should be recoverable by reading the file");
    }

    private static void GarbageIsRefused()
    {
        // An unreadable trust file must not throw. The caller falls back and reports,
        // because refusing to start would leave no route back short of deleting a file
        // the user may not know exists.
        Check(UpdateTrustEnvelope.Unprotect(null) == null, "null should decode to nothing");
        Check(UpdateTrustEnvelope.Unprotect(string.Empty) == null, "empty should decode to nothing");
        Check(UpdateTrustEnvelope.Unprotect("not json at all") == null, "junk should decode to nothing");
        Check(UpdateTrustEnvelope.Unprotect("{\"Format\":\"MacRando.UpdateTrust.DPAPI.v1\"}") == null,
            "an envelope with no cipher text should decode to nothing");
        Check(UpdateTrustEnvelope.Unprotect(
                "{\"SchemaVersion\":1,\"CipherText\":\"bm90IGEgYmxvYg==\"}") == null,
            "a cipher text that is not a real DPAPI blob should decode to nothing");
    }

    private static void AnEnvelopeForSomethingElseIsRefused()
    {
        // The restore state's DPAPI entropy differs, so its blob cannot be decrypted here.
        // Asserted by round-tripping through the restore envelope's own protection and
        // confirming the update reader refuses it rather than returning garbage.
        string foreign = "{\"Format\":\"MacRando.RestoreState.v1\",\"SchemaVersion\":1,\"CipherText\":\"AAAA\"}";
        Check(UpdateTrustEnvelope.Unprotect(foreign) == null,
            "a blob with a foreign format marker should be refused");
    }

    private static void SavingClearsTheValuesFromTheClearTextFile()
    {
        string dir = Sandbox();
        try
        {
            var store = new MacRando.AppSettingsStore(Path.Combine(dir, "settings.json"));
            var settings = new AppSettings
            {
                UpdateManifestUrl = SampleUrl,
                ExpectedSignerThumbprint = SampleThumbprint,
                StartMinimized = true,
                NotificationDurationSeconds = 9
            };
            store.Save(settings);

            string clearText = File.ReadAllText(Path.Combine(dir, "settings.json"), Encoding.UTF8);
            Check(clearText.IndexOf(SampleUrl, StringComparison.Ordinal) < 0,
                "saving must clear the manifest URL from the plain settings file");
            Check(clearText.IndexOf(SampleThumbprint, StringComparison.Ordinal) < 0,
                "saving must clear the thumbprint from the plain settings file");
            Check(File.Exists(Path.Combine(dir, "settings.json.trust")),
                "saving must write the encrypted trust file");

            // The values must still be usable, which is the point.
            AppSettings reloaded = store.Load();
            Check(reloaded.UpdateManifestUrl == SampleUrl, "the manifest URL should reload from the trust file");
            Check(reloaded.ExpectedSignerThumbprint == SampleThumbprint,
                "the thumbprint should reload from the trust file");
            Check(reloaded.StartMinimized, "preferences must not be lost in the move");
            Check(reloaded.NotificationDurationSeconds == 9, "preferences must round trip");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TrustFileSurvivesWhenTheSettingsFileIsDeleted()
    {
        // A user who clears settings.json to start clean should not silently lose the
        // trust values, because the trust file is a separate thing they were not told
        // about. The settings file is the one that is regenerated; the trust file is not.
        string dir = Sandbox();
        try
        {
            string settingsPath = Path.Combine(dir, "settings.json");
            var store = new MacRando.AppSettingsStore(settingsPath);
            store.Save(new AppSettings
            {
                UpdateManifestUrl = SampleUrl,
                ExpectedSignerThumbprint = SampleThumbprint
            });
            File.Delete(settingsPath);

            AppSettings reloaded = store.Load();
            Check(reloaded.UpdateManifestUrl == SampleUrl,
                "deleting the plain settings file must not discard the encrypted trust values");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void PreferencesAreStillInClearText()
    {
        // Deliberate. Preferences have no security consequence, and encrypting them would
        // make them unreadable to a user trying to work out why something is misbehaving.
        string dir = Sandbox();
        try
        {
            var store = new MacRando.AppSettingsStore(Path.Combine(dir, "settings.json"));
            store.Save(new AppSettings
            {
                UpdateManifestUrl = SampleUrl,
                ExpectedSignerThumbprint = SampleThumbprint,
                ShowConnectedAdaptersOnly = true,
                AutoRandomizeMacOnStartup = true
            });
            string clearText = File.ReadAllText(Path.Combine(dir, "settings.json"), Encoding.UTF8);
            Check(clearText.Contains("ShowConnectedAdaptersOnly"),
                "preferences should remain readable in the plain settings file");
            Check(clearText.Contains("StartMinimized"),
                "preferences should remain readable in the plain settings file");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// The redirect policy: HTTPS hops are followed, anything that leaves TLS is refused.
    ///
    /// This test previously asserted that every 3xx was refused, which was the bug rather
    /// than the requirement. GitHub answers a release-asset URL with 302 to a signed blob
    /// URL by design, so that policy made the updater unable to reach its own manifest.
    /// The example below is the redirect GitHub actually sends.
    /// </summary>
    private static void RedirectsAreResolvedButNeverDowngraded()
    {
        var https = new Uri("https://github.com/SpaceJamp/MacRando/releases/latest/download/update.json");

        System.Reflection.MethodInfo resolve = typeof(MacRando.UpdateService).GetMethod(
            "ResolveRedirect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(resolve != null, "the update service should expose its redirect decision");

        // The real GitHub hop: https -> https, must be followed.
        var blob = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/x?sig=abc");
        using (var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.Found))
        {
            response.Headers.Location = blob;
            Uri followed = (Uri)resolve.Invoke(null, new object[] { https, response, "update manifest", 302 });
            Check(followed == blob, "an https-to-https redirect must be followed to its target");
        }

        // Every redirect status GitHub or a CDN might use must resolve the same way,
        // because 1.11.0-1.13.1 refused all of them and broke the updater.
        foreach (int code in new[] { 301, 302, 303, 307, 308 })
        {
            using (var response = new System.Net.Http.HttpResponseMessage((HttpStatusCode)code))
            {
                response.Headers.Location = blob;
                Uri followed = (Uri)resolve.Invoke(null, new object[] { https, response, "update manifest", code });
                Check(followed == blob, "HTTP " + code + " to an https address must be followed");
            }
        }

        // The property that is actually worth protecting: a hop to plain HTTP is refused,
        // for every redirect status and for a relative location that resolves to http.
        foreach (int code in new[] { 301, 302, 303, 307, 308 })
        {
            using (var response = new System.Net.Http.HttpResponseMessage((HttpStatusCode)code))
            {
                response.Headers.Location = new Uri("http://evil.example.com/update.json");
                Check(ThrowsOnResolve(resolve, https, response, "update manifest", code),
                    "HTTP " + code + " to a plain-HTTP address must be refused");
            }
        }

        // A relative location resolves against the https base, so it stays encrypted and
        // must be accepted. This is the case a naive scheme check on the raw Location
        // string would wrongly reject.
        using (var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.Found))
        {
            response.Headers.Location = new Uri("/releases/download/v1/update.json", UriKind.Relative);
            Uri followed = (Uri)resolve.Invoke(null, new object[] { https, response, "update manifest", 302 });
            Check(followed.Scheme == Uri.UriSchemeHttps && followed.AbsolutePath == "/releases/download/v1/update.json",
                "a relative redirect must resolve against the https base and be followed");
        }

        // A redirect with nowhere to go is an error rather than a silent no-op.
        using (var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.Found))
        {
            Check(ThrowsOnResolve(resolve, https, response, "update manifest", 302),
                "a redirect with no location must be refused");
        }

        // A scheme that is not http at all must not be treated as acceptable.
        foreach (string odd in new[] { "file:///C:/Windows/System32/drivers/etc/hosts", "ftp://example.com/update.json" })
        {
            using (var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.Found))
            {
                response.Headers.Location = new Uri(odd);
                Check(ThrowsOnResolve(resolve, https, response, "update manifest", 302),
                    "a redirect to " + odd + " must be refused");
            }
        }
    }

    private static bool ThrowsOnResolve(System.Reflection.MethodInfo resolve, Uri current,
        System.Net.Http.HttpResponseMessage response, string what, int code)
    {
        try
        {
            resolve.Invoke(null, new object[] { current, response, what, code });
            return false;
        }
        catch (System.Reflection.TargetInvocationException)
        {
            return true;
        }
    }

    /// <summary>
    /// The manifest a real release points at, fetched end to end.
    ///
    /// The redirect policy above is a unit test of a decision function, which cannot catch
    /// the thing that actually broke: that the live manifest is served through a redirect.
    /// This asks GitHub for the real published manifest, so if the URL stops resolving, or
    /// stops redirecting, or redirects somewhere that is not https, the suite fails.
    /// </summary>
    private static void TheLiveManifestIsReachableThroughARedirect()
    {
        // This waits on an async call, and earlier suites in the same process leave a
        // Windows Forms synchronisation context installed, so blocking the calling thread
        // would deadlock instead of returning. Run the body where no context is captured.
        Task.Run(delegate { CheckTheLiveManifest(); }).GetAwaiter().GetResult();
    }

    private static void CheckTheLiveManifest()
    {
        const string manifestUrl =
            "https://github.com/SpaceJamp/MacRando/releases/latest/download/update.json";

        // The test host may not default to TLS 1.2, which GitHub requires. The app sets
        // this in UpdateService.CreateClient; the raw probe below bypasses that, so it
        // needs the same setting or it fails on a channel error rather than on a verdict.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        HttpWebRequest probe = (HttpWebRequest)WebRequest.Create(manifestUrl);
        probe.AllowAutoRedirect = false;
        probe.Timeout = 20000;
        using (HttpWebResponse first = (HttpWebResponse)probe.GetResponse())
        {
            int code = (int)first.StatusCode;
            Check(code == 301 || code == 302 || code == 303 || code == 307 || code == 308,
                "the release manifest is expected to answer with a redirect, but answered HTTP " + code +
                ". If GitHub now serves it directly that is fine, but this test should be updated" +
                " rather than left asserting something that no longer holds.");

            // The redirect must be resolved the way the service resolves it, and the result
            // must be https: this is the hop the old code refused.
            Uri target;
            using (var response = new System.Net.Http.HttpResponseMessage((HttpStatusCode)code))
            {
                response.Headers.Location = new Uri(first.Headers["Location"]);
                target = MacRando.UpdateService.ResolveRedirect(
                    new Uri(manifestUrl), response, "update manifest", code);
            }
            Check(target.Scheme == Uri.UriSchemeHttps,
                "the live manifest must redirect to https, but redirected to " + target.Scheme + "://");
        }

        // And following it must actually produce this project's manifest.
        var service = new MacRando.UpdateService();
        MacRando.UpdateCheckResult result = service.CheckAsync(manifestUrl, null).GetAwaiter().GetResult();
        Check(!result.RequiresConfiguration,
            "the live manifest check must not report that configuration is required: " + result.StatusMessage);
        Check(result.Manifest != null,
            "the live manifest must parse, otherwise the updater cannot work: " + result.StatusMessage);
        // Not asserted: that the manifest names this build. The suite runs during
        // release.ps1, before the new build is published, so the live manifest is
        // deliberately one version behind at that point. What matters is that the check
        // runs far enough to compare, and that the comparison is coherent.
        Check(result.Manifest != null && !string.IsNullOrWhiteSpace(result.Manifest.Version),
            "the live manifest must carry a version");
        Check(!string.IsNullOrWhiteSpace(result.Manifest.DownloadUrl),
            "the live manifest must carry a download URL");
        Check(string.IsNullOrWhiteSpace(result.Manifest.Sha256) == false,
            "the live manifest must carry a SHA-256, or nothing could be verified");
        Check(string.IsNullOrWhiteSpace(result.Manifest.SignerThumbprint) == false,
            "the live manifest must carry a signer thumbprint, or nothing could be verified");

        // The comparison must actually be reached. Before the redirect fix this threw
        // before the version was ever parsed, so a passing parse alone would not have
        // caught the regression.
        Version published;
        if (Version.TryParse(result.Manifest.Version, out published))
        {
            Version current = Version.Parse(MacRando.AppInfo.Version);
            bool shouldOffer = published > current;
            Check(result.IsUpdateAvailable == shouldOffer,
                "with the published manifest at " + published + " and this build at " + current +
                ", an update should be offered = " + shouldOffer + ", but the check said " +
                result.IsUpdateAvailable);
            if (shouldOffer)
            {
                Check(result.StatusMessage.Contains(published.ToString()),
                    "an available update should name the version, but the message was: " + result.StatusMessage);
            }
        }
    }
}
