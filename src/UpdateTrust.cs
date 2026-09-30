using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MacRando
{
    /// <summary>
    /// The envelope for the two values that decide where updates come from and whose
    /// signature they must carry.
    ///
    /// These are in a separate file from the rest of the settings, and encrypted, because
    /// together they are the trust anchor for code execution: MacRando runs elevated, so
    /// anything able to rewrite the manifest URL can point an update at a server of its
    /// choosing. The Authenticode check limits what that achieves, since the download
    /// still has to be signed by the expected thumbprint, but the redirect itself should
    /// not be writable by anything that is not the user.
    ///
    /// The remainder of the settings stay in clear text. They are preferences with no
    /// security consequence, and encrypting them would make them unreadable when a user
    /// is trying to work out why something is misbehaving.
    /// </summary>
    internal sealed class UpdateTrustEnvelope
    {
        public const string ProtectedFormat = "MacRando.UpdateTrust.DPAPI.v1";

        public string Format { get; set; }
        public int SchemaVersion { get; set; }

        /// <summary>Named to avoid colliding with the DPAPI type of the same name.</summary>
        public string CipherText { get; set; }

        /// <summary>
        /// Tied to the product and the field names, so a blob produced for a different
        /// purpose cannot be substituted for this one even by something that can call
        /// DPAPI as the same user.
        /// </summary>
        private static readonly byte[] ProtectionEntropy = Encoding.UTF8.GetBytes("MacRando.UpdateTrust.v1");

        public string UpdateManifestUrl { get; set; }
        public string ExpectedSignerThumbprint { get; set; }

        public static bool IsProtectedFormat(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.IndexOf(ProtectedFormat, StringComparison.Ordinal) >= 0;
        }

        public string Protect()
        {
            var envelope = new UpdateTrustEnvelope
            {
                Format = ProtectedFormat,
                SchemaVersion = 1,
                UpdateManifestUrl = UpdateManifestUrl ?? string.Empty,
                ExpectedSignerThumbprint = ExpectedSignerThumbprint ?? string.Empty
            };
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            byte[] clear = Encoding.UTF8.GetBytes(serializer.Serialize(new UpdateTrustPayload
            {
                UpdateManifestUrl = envelope.UpdateManifestUrl,
                ExpectedSignerThumbprint = envelope.ExpectedSignerThumbprint
            }));
            byte[] protectedBytes = ProtectedData.Protect(
                clear,
                ProtectionEntropy,
                DataProtectionScope.CurrentUser);
            // Serialized through the record, not the envelope: the envelope's public
            // properties still hold the clear values at this point.
            return serializer.Serialize(new UpdateTrustRecord
            {
                Format = ProtectedFormat,
                SchemaVersion = 1,
                CipherText = Convert.ToBase64String(protectedBytes)
            });
        }

        /// <summary>
        /// Returns the decoded payload, or null when the blob cannot be read. A null here
        /// is not fatal: the caller falls back to clear text and reports it, because
        /// refusing to start over an unreadable trust file would leave the user with no
        /// way to recover short of deleting a file they may not know exists.
        /// </summary>
        public static UpdateTrustPayload Unprotect(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            try
            {
                UpdateTrustRecord record =
                    new System.Web.Script.Serialization.JavaScriptSerializer()
                        .Deserialize<UpdateTrustRecord>(text);
                if (record == null || record.SchemaVersion != 1 ||
                    record.Format != ProtectedFormat ||
                    string.IsNullOrWhiteSpace(record.CipherText))
                {
                    return null;
                }
                byte[] clear = ProtectedData.Unprotect(
                    Convert.FromBase64String(record.CipherText),
                    ProtectionEntropy,
                    DataProtectionScope.CurrentUser);
                return new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<UpdateTrustPayload>(Encoding.UTF8.GetString(clear));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The exact shape written to disk. Deliberately separate from the envelope, whose
    /// public properties are set before encryption and would otherwise be serialized
    /// alongside the cipher text and put the manifest URL back in the clear.
    /// </summary>
    internal sealed class UpdateTrustRecord
    {
        public string Format { get; set; }
        public int SchemaVersion { get; set; }
        public string CipherText { get; set; }
    }

    /// <summary>
    /// The two protected values on their own, so the encrypted blob holds only what it
    /// must and the record's own fields cannot be confused with the payload.
    /// </summary>
    internal sealed class UpdateTrustPayload
    {
        public string UpdateManifestUrl { get; set; }
        public string ExpectedSignerThumbprint { get; set; }
    }
}
