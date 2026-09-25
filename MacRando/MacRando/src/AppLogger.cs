using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MacRando
{
    internal static class AppLogger
    {
        private static readonly object Sync = new object();
        private static readonly Regex MacPattern = new Regex(@"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])", RegexOptions.Compiled);
        private static readonly Regex CompactMacPattern = new Regex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{12}(?![0-9A-Fa-f])", RegexOptions.Compiled);
        private static readonly Regex IPv4Pattern = new Regex(@"(?<![0-9])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9])", RegexOptions.Compiled);

        public static string LogPath
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MacRando");
                return Path.Combine(root, "macrando.log");
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warning(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception error)
        {
            Write("ERROR", message, error);
        }

        public static string MaskMac(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            string normalized = value.Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
            if (normalized.Length != 12)
            {
                return "********";
            }
            return normalized.Substring(0, 2) + "********" + normalized.Substring(10, 2);
        }

        public static string MaskIp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            return IPv4Pattern.Replace(value, "[IP]");
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            string sanitized = MacPattern.Replace(value, "[MAC]");
            sanitized = CompactMacPattern.Replace(sanitized, "[MAC]");
            sanitized = IPv4Pattern.Replace(sanitized, "[IP]");
            return sanitized.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < 1024 * 1024)
                {
                    return;
                }
                string archived = Path.Combine(
                    Path.GetDirectoryName(LogPath),
                    "macrando-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
                File.Move(LogPath, archived);
            }
            catch
            {
            }
        }

        private static void Write(string level, string message, Exception error)
        {
            try
            {
                lock (Sync)
                {
                    string directory = Path.GetDirectoryName(LogPath);
                    Directory.CreateDirectory(directory);
                    RotateIfNeeded();
                    var entry = new Dictionary<string, object>
                    {
                        { "timestampUtc", DateTime.UtcNow.ToString("o") },
                        { "level", level },
                        { "version", AppInfo.Version },
                        { "message", Sanitize(message) }
                    };
                    if (error != null)
                    {
                        entry["exception"] = Sanitize(error.GetType().Name + ": " + error.Message);
                    }
                    string line = new JavaScriptSerializer().Serialize(entry) + Environment.NewLine;
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never interrupt a network operation.
            }
        }
    }
}
