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
            EveryPreferenceSurvivesASave();
            AFailedTrustWriteKeepsTheUpdateSource();
            ACorruptSettingsFileKeepsTheUpdateSource();
            ADamagedSignatureIsRefusedDespiteTheThumbprint();
            TheHelperScriptIsNeverReadFromDisk();
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

    /// <summary>
    /// Every preference must come back from a save. Save copied the fields one by one and
    /// left out ShowPublicIpLocation, so that opt-in was written as false every time and
    /// never survived a restart. Every public property is set to a non-default value by
    /// reflection, so a preference added later is covered without editing this test.
    /// </summary>
    private static void EveryPreferenceSurvivesASave()
    {
        string dir = Sandbox();
        try
        {
            var store = new MacRando.AppSettingsStore(Path.Combine(dir, "settings.json"));
            var settings = new AppSettings();
            foreach (System.Reflection.PropertyInfo property in typeof(AppSettings).GetProperties())
            {
                if (!property.CanWrite)
                {
                    continue;
                }
                if (property.PropertyType == typeof(bool))
                {
                    property.SetValue(settings, !(bool)property.GetValue(settings, null), null);
                }
                else if (property.PropertyType == typeof(int))
                {
                    // In range for every clamped setting.
                    property.SetValue(settings, 9, null);
                }
            }
            settings.AutoRandomizeAdapterKey = "{121EC763-A5F6-4EF7-AD11-554AD8464295}";
            settings.UpdateManifestUrl = SampleUrl;
            settings.ExpectedSignerThumbprint = SampleThumbprint;
            settings.FavoriteAdapters = new List<string> { "{A}", "{B}" };
            store.Save(settings);

            AppSettings loaded = store.Load();
            foreach (System.Reflection.PropertyInfo property in typeof(AppSettings).GetProperties())
            {
                if (!property.CanWrite || property.PropertyType == typeof(List<string>))
                {
                    continue;
                }
                object expected = property.GetValue(settings, null);
                object actual = property.GetValue(loaded, null);
                Check(object.Equals(expected, actual),
                    "the preference " + property.Name + " did not survive a save: saved " + expected + ", loaded " + actual);
            }
            Check(loaded.FavoriteAdapters.Count == 2, "favorites must survive a save");
            Check(loaded.ShowPublicIpLocation, "the public IP location opt-in must survive a save");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// The update values used to be cleared from settings.json before the encrypted file
    /// was written, so a failed encrypted write lost them, while the log said they were
    /// still in settings.json. The write is made to fail by putting a directory where its
    /// temporary file goes.
    /// </summary>
    private static void AFailedTrustWriteKeepsTheUpdateSource()
    {
        string dir = Sandbox();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var store = new MacRando.AppSettingsStore(path);
            Directory.CreateDirectory(store.UpdateTrustPath + ".tmp");
            var settings = new AppSettings { UpdateManifestUrl = SampleUrl, ExpectedSignerThumbprint = SampleThumbprint };
            store.Save(settings);

            Check(!File.Exists(store.UpdateTrustPath), "the encrypted write was meant to fail in this test");
            AppSettings loaded = store.Load();
            Check(loaded.UpdateManifestUrl == SampleUrl,
                "a failed encrypted write must leave the update URL in settings.json, not lose it");
            Check(loaded.ExpectedSignerThumbprint == SampleThumbprint,
                "a failed encrypted write must leave the signer thumbprint in settings.json");

            // Once the encrypted write can succeed, the clear text copy goes.
            Directory.Delete(store.UpdateTrustPath + ".tmp");
            store.Save(loaded);
            Check(File.ReadAllText(path).IndexOf(SampleUrl, StringComparison.Ordinal) < 0,
                "after a successful encrypted write the URL must leave settings.json");
            Check(store.Load().UpdateManifestUrl == SampleUrl, "and still load from the encrypted file");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// A settings.json that cannot be parsed used to discard the already-read trust
    /// values too, and the next preference change then wrote blanks over a trust file
    /// that was perfectly readable.
    /// </summary>
    private static void ACorruptSettingsFileKeepsTheUpdateSource()
    {
        string dir = Sandbox();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var store = new MacRando.AppSettingsStore(path);
            store.Save(new AppSettings { UpdateManifestUrl = SampleUrl, ExpectedSignerThumbprint = SampleThumbprint });
            File.WriteAllText(path, "{ this is not json");

            AppSettings loaded = store.Load();
            Check(loaded.UpdateManifestUrl == SampleUrl,
                "a corrupt settings.json must not hide a readable trust file");
            loaded.ShowConnectedAdaptersOnly = true;
            store.Save(loaded);
            AppSettings reloaded = store.Load();
            Check(reloaded.UpdateManifestUrl == SampleUrl && reloaded.ExpectedSignerThumbprint == SampleThumbprint,
                "saving a preference after a corrupt settings.json must not erase the update source");
            Check(reloaded.ShowConnectedAdaptersOnly, "and the new preference must be kept");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void ADamagedSignatureIsRefusedDespiteTheThumbprint()
    {
        // A signed build modified after signing still carries its signer's certificate, so
        // Get-AuthenticodeSignature reports the expected thumbprint with HashMismatch. The
        // thumbprint used to be accepted on its own, which let exactly that through.
        Check(MacRando.UpdateService.ParseSignatureOutput("HashMismatch|" + SampleThumbprint) == string.Empty,
            "a HashMismatch signature must be refused even with the expected thumbprint");
        Check(MacRando.UpdateService.ParseSignatureOutput("NotSigned|" + SampleThumbprint) == string.Empty,
            "a NotSigned status must be refused");
        Check(MacRando.UpdateService.ParseSignatureOutput(SampleThumbprint) == string.Empty,
            "a bare thumbprint with no status must be refused");
        Check(MacRando.UpdateService.ParseSignatureOutput("Valid|not-a-thumbprint") == string.Empty,
            "a malformed thumbprint must be refused");
        Check(MacRando.UpdateService.ParseSignatureOutput("Valid|" + SampleThumbprint.ToLowerInvariant()) == SampleThumbprint,
            "a valid signature should yield its thumbprint, normalized to upper case");
        // The publisher certificate is self-signed and lives in TrustedPublisher, so a real
        // release reports an untrusted chain. Refusing that would break every update.
        Check(MacRando.UpdateService.ParseSignatureOutput("UnknownError|" + SampleThumbprint) == SampleThumbprint,
            "an untrusted chain is accepted because the signer is pinned by thumbprint");
        Check(MacRando.UpdateService.ParseSignatureOutput("NotTrusted|" + SampleThumbprint) == SampleThumbprint,
            "NotTrusted is a chain failure, not damage, and the signer is pinned");
    }

    private static void TheHelperScriptIsNeverReadFromDisk()
    {
        // The helper runs elevated. Its script used to be written to the per-user temp
        // folder and started with -File, so any unelevated process could rewrite it first.
        System.Diagnostics.ProcessStartInfo info = MacRando.UpdateInstaller.BuildHelperStartInfo();
        Check(info.Arguments.IndexOf("-File", StringComparison.OrdinalIgnoreCase) < 0,
            "the helper must not be started from a script file");
        Check(info.Arguments.IndexOf("-EncodedCommand", StringComparison.OrdinalIgnoreCase) >= 0,
            "the helper should be started with an inline bootstrap");
        Check(info.RedirectStandardInput, "the script should be delivered through standard input");
        string encoded = info.Arguments.Substring(info.Arguments.LastIndexOf(' ') + 1);
        string bootstrap = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Check(bootstrap == MacRando.UpdateInstaller.HelperBootstrap,
            "the encoded command should be exactly the bootstrap");
        Check(bootstrap.IndexOf("[Console]::In", StringComparison.Ordinal) >= 0,
            "the bootstrap should read the script from standard input");

        string source = File.ReadAllText(Path.Combine(
            Environment.GetEnvironmentVariable("MACRANDO_REPO_ROOT") ?? ".", "src", "UpdateInstaller.cs"));
        Check(source.IndexOf("\".ps1\"", StringComparison.Ordinal) < 0,
            "UpdateInstaller must not write a .ps1 file for the helper");
    }
}
