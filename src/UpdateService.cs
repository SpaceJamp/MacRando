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
                string manifestJson = await Client.GetStringAsync(manifestUri);
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
                byte[] payload = await Client.GetByteArrayAsync(downloadUri);
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

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.Version);
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
