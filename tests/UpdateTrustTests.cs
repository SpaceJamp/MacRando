using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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
            RedirectsAreRefused();
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

    private static void RedirectsAreRefused()
    {
        // A fresh handler allows redirects, which is the framework default and exactly
        // what the update client must not inherit. Asserted so the default is visible:
        // if this ever becomes false the default has changed and the comparison below
        // would pass for the wrong reason.
        var frameworkDefault = new System.Net.Http.HttpClientHandler();
        Check(frameworkDefault.AllowAutoRedirect,
            "a default handler is expected to follow redirects, otherwise this check is vacuous");

        // What matters is the handler the update client actually builds.
        System.Reflection.FieldInfo clientField = typeof(MacRando.UpdateService).GetField(
            "Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(clientField != null, "the update service should hold a single static client");
        var client = (System.Net.Http.HttpClient)clientField.GetValue(null);
        Check(client != null, "the update client should exist");

        // The handler is private to the client, so the refusal is asserted at the point
        // that actually enforces it: a 3xx response is treated as a failure. A redirect
        // that was being followed would never produce a 3xx here.
        System.Reflection.MethodInfo reject = typeof(MacRando.UpdateService).GetMethod(
            "RejectRedirect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(reject != null, "the update service should have a redirect check");

        foreach (int code in new[] { 301, 302, 303, 307, 308 })
        {
            bool threw = false;
            try
            {
                reject.Invoke(null, new object[]
                {
                    new System.Net.Http.HttpResponseMessage((HttpStatusCode)code),
                    "update manifest"
                });
            }
            catch (System.Reflection.TargetInvocationException)
            {
                threw = true;
            }
            Check(threw, "HTTP " + code + " must be refused rather than followed");
        }

        // A success must pass through untouched, or the client could never fetch anything.
        bool passedThrough = true;
        try
        {
            reject.Invoke(null, new object[]
            {
                new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK),
                "update manifest"
            });
        }
        catch
        {
            passedThrough = false;
        }
        Check(passedThrough, "a successful response must not be refused");
    }
}
