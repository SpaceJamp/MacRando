using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MacRando
{
    internal sealed class UpdateService
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly Regex Sha256Pattern = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex ThumbprintPattern = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

        public async Task<UpdateCheckResult> CheckAsync(string manifestUrl, string expectedSignerThumbprint)
        {
            UpdateCheckResult result = new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                IsVerified = false,
                RequiresConfiguration = true,
                StatusMessage = "No update manifest URL is configured."
            };
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return result;
            }

            Uri manifestUri;
            if (!Uri.TryCreate(manifestUrl.Trim(), UriKind.Absolute, out manifestUri) ||
                !string.Equals(manifestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                result.StatusMessage = "The update manifest URL must be an absolute HTTPS URL.";
                return result;
            }

            try
            {
                string manifestJson = await ReadAllTextStrict(manifestUri);
                UpdateManifest manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(manifestJson);
                string validationError = ValidateManifest(manifest);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    result.StatusMessage = validationError;
                    result.RequiresConfiguration = false;
                    return result;
                }

                result.RequiresConfiguration = false;
                result.Manifest = manifest;
                Version currentVersion;
                Version manifestVersion;
                if (!Version.TryParse(AppInfo.Version, out currentVersion) || !Version.TryParse(manifest.Version, out manifestVersion))
                {
                    result.StatusMessage = "The update manifest contains an invalid version number.";
                    return result;
                }
                if (manifestVersion <= currentVersion)
                {
                    result.StatusMessage = "MacRando " + AppInfo.DisplayVersion + " is up to date.";
                    return result;
                }

                result.IsUpdateAvailable = true;
                Uri downloadUri;
                if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out downloadUri) ||
                    !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsUpdateAvailable = false;
                    result.StatusMessage = "The update download URL must be an absolute HTTPS URL.";
                    return result;
                }

                string downloadDirectory = Path.Combine(Path.GetTempPath(), "MacRando", "updates");
                Directory.CreateDirectory(downloadDirectory);
                string fileName = "MacRando-" + manifest.Version + ".exe";
                string destinationPath = Path.Combine(downloadDirectory, fileName);
                byte[] payload = await ReadAllBytesStrict(downloadUri);
                File.WriteAllBytes(destinationPath, payload);

                string actualHash;
                using (SHA256 sha = SHA256.Create())
                {
                    actualHash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", string.Empty);
                }
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SafeDelete(destinationPath);
                    result.IsUpdateAvailable = false;
                    result.StatusMessage = "The downloaded update failed SHA-256 verification.";
                    return result;
                }

                string actualThumbprint = GetAuthenticodeThumbprint(destinationPath);
                string manifestThumbprint = manifest.SignerThumbprint.Replace(" ", "").ToUpperInvariant();
                string configuredThumbprint = (expectedSignerThumbprint ?? string.Empty).Replace(" ", "").ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(actualThumbprint) ||
                    !string.Equals(actualThumbprint, manifestThumbprint, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(configuredThumbprint) &&
                     !string.Equals(actualThumbprint, configuredThumbprint, StringComparison.OrdinalIgnoreCase)))
                {
                    SafeDelete(destinationPath);
                    result.IsUpdateAvailable = false;
                    result.StatusMessage = "The downloaded update failed Authenticode signer verification.";
                    return result;
                }

                result.IsVerified = true;
                result.LocalDownloadPath = destinationPath;
                result.StatusMessage = "MacRando " + manifest.Version + " was downloaded and verified. It was not installed automatically.";
                return result;
            }
            catch (Exception error)
            {
                result.RequiresConfiguration = false;
                result.StatusMessage = "Update check failed: " + AppLogger.Sanitize(error.Message);
                return result;
            }
        }

        private static string ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null)
            {
                return "The update manifest is empty.";
            }
            Version parsedVersion;
            if (string.IsNullOrWhiteSpace(manifest.Version) || !Version.TryParse(manifest.Version.Trim(), out parsedVersion))
            {
                return "The update manifest has an invalid version.";
            }
            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return "The update manifest has no download URL.";
            }
            if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Sha256Pattern.IsMatch(manifest.Sha256.Trim()))
            {
                return "The update manifest has an invalid SHA-256 value.";
            }
            if (string.IsNullOrWhiteSpace(manifest.SignerThumbprint) || !ThumbprintPattern.IsMatch(manifest.SignerThumbprint.Replace(" ", "")))
            {
                return "The update manifest has an invalid signer thumbprint.";
            }
            return string.Empty;
        }

        private const int MaxRedirects = 5;

        /// <summary>
        /// Sends the request, following redirects by hand.
        ///
        /// This used to refuse every 3xx outright, on the reasoning that a redirect could
        /// downgrade the connection to plain HTTP. That reasoning is sound but the
        /// implementation was wrong about GitHub: a release-asset URL answers 302 to a
        /// signed blob URL as a matter of design, so every build from 1.11.0 onward
        /// refused its own update manifest and the updater never worked against a real
        /// release. The old message even told the user to set the manifest URL to the
        /// final address, which is not a thing anyone can do: that address carries a
        /// short-lived signature and expires.
        ///
        /// What is worth protecting is that the transfer never leaves TLS, so a redirect
        /// is followed only while every hop is HTTPS, and the chain is capped. Integrity
        /// does not rest on this alone in any case: the manifest is fetched over TLS and
        /// the download is checked against both the SHA-256 and the Authenticode signer
        /// named in that manifest, so a hostile redirect cannot yield a payload that
        /// passes both checks.
        /// </summary>
        /// <summary>
        /// Works out where a 3xx points, and refuses it if the hop would leave TLS.
        ///
        /// Split out from the sending loop so the decision can be tested directly. It used
        /// to be inline, and the only way to assert anything about it was to run a live
        /// HTTPS server, which is why the original test asserted a method that no longer
        /// existed.
        /// </summary>
        internal static Uri ResolveRedirect(Uri current, HttpResponseMessage response, string what, int code)
        {
            var location = response.Headers.Location;
            if (location == null)
            {
                throw new InvalidOperationException(
                    "The " + what + " URL returned HTTP " + code + " with no location to follow.");
            }

            Uri next;
            if (location.IsAbsoluteUri)
            {
                next = new Uri(location.AbsoluteUri, UriKind.Absolute);
            }
            else
            {
                try
                {
                    // Resolving against an https base keeps a relative location on https.
                    next = new Uri(current, location);
                }
                catch (UriFormatException)
                {
                    throw new InvalidOperationException(
                        "The " + what + " URL returned HTTP " + code + " with an unusable location.");
                }
            }

            if (!string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The " + what + " URL redirected to a non-HTTPS address (" + next.Scheme +
                    "://), which is refused so the transfer cannot be downgraded to plain HTTP.");
            }
            return next;
        }

        private static async Task<HttpResponseMessage> SendAllowingHttpsRedirectsAsync(Uri uri, string what)
        {
            Uri current = uri;
            for (int hop = 0; ; hop++)
            {
                HttpResponseMessage response = await Client.GetAsync(current);
                int code = (int)response.StatusCode;
                if (code < 300 || code >= 400)
                {
                    return response;
                }

                Uri next = ResolveRedirect(current, response, what, code);
                if (hop >= MaxRedirects)
                {
                    response.Dispose();
                    throw new InvalidOperationException(
                        "The " + what + " URL redirected more than " + MaxRedirects + " times.");
                }

                response.Dispose();
                current = next;
            }
        }

        private static async Task<string> ReadAllTextStrict(Uri uri)
        {
            using (HttpResponseMessage response = await SendAllowingHttpsRedirectsAsync(uri, "update manifest"))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "The update manifest request failed with HTTP " + (int)response.StatusCode + ".");
                }
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static async Task<byte[]> ReadAllBytesStrict(Uri uri)
        {
            using (HttpResponseMessage response = await SendAllowingHttpsRedirectsAsync(uri, "update download"))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "The update download failed with HTTP " + (int)response.StatusCode + ".");
                }
                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Automatic redirection stays off, but redirects are now followed by hand in
            // SendAllowingHttpsRedirectsAsync rather than refused. The reason is on the
            // handler and in that method together: the built-in follower cannot be told to
            // check the scheme of each hop, and refusing every redirect broke the updater
            // against GitHub, which 302s release-asset URLs by design. It stays off here
            // also because disabling it globally on ServicePointManager would change
            // behaviour for every other request in the process.
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.Version);
            // A cached manifest can hide a release published moments ago, which would leave
            // a user stuck on an older build with no indication why.
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true
            };
            return client;
        }

        private static string GetAuthenticodeThumbprint(string path)
        {
            string script =
                "$signature = Get-AuthenticodeSignature -LiteralPath '" + path.Replace("'", "''") + "'; " +
                "if ($signature.SignerCertificate) { [Console]::Write($signature.SignerCertificate.Thumbprint) }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return string.Empty;
                }
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(15000);
                return ThumbprintPattern.IsMatch(output ?? string.Empty) ? output.ToUpperInvariant() : string.Empty;
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
