using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MacRando
{
    internal static class LicenseInfo
    {
        public const string ShortName = "PNC-1.2";
        public const string Name = "Personal and Non-Commercial Use License";
        public const string Url = "https://github.com/SpaceJamp/MacRando/blob/main/LICENSE";
        public const string Copyright = "Copyright (c) 2026 Jeffrey Horne";
        public const string ResourceName = "MacRando.LICENSE.txt";

        public static string Notice
        {
            get { return Copyright + "  •  License: " + ShortName; }
        }

        public static string Summary
        {
            get
            {
                return Copyright + Environment.NewLine +
                    "Licensed under the " + Name + " (" + ShortName + ")." + Environment.NewLine +
                    Environment.NewLine +
                    "You may use and modify this software for personal, non-commercial purposes, and " +
                    "share it free of charge under the same license with attribution. If you share a " +
                    "modified version, you must also provide its source code. Any commercial use, " +
                    "including use by a business, school, nonprofit, or government body, requires a " +
                    "separate written license from the author." + Environment.NewLine +
                    Environment.NewLine +
                    "This software is provided AS IS, without warranty of any kind. " +
                    "MacRando changes live network adapter settings and requires " +
                    "administrator rights, so you are responsible for testing it on a network you " +
                    "are permitted to reconfigure and for complying with applicable laws and policies." + Environment.NewLine +
                    Environment.NewLine +
                    "Full license text: " + Url + Environment.NewLine +
                    Environment.NewLine +
                    "Inspired by the general concept of MAC-address randomizing tools. " +
                    "MacRando is an independent implementation and is not affiliated with, " +
                    "endorsed by, or derived from any other project.";
            }
        }

        public static string GetFullText()
        {
            try
            {
                Assembly assembly = typeof(LicenseInfo).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static string GetDisplayText()
        {
            string full = GetFullText();
            if (string.IsNullOrWhiteSpace(full))
            {
                return Summary;
            }
            return Copyright + Environment.NewLine +
                "Licensed under the " + Name + " (" + ShortName + ")." +
                Environment.NewLine + Environment.NewLine +
                "Full license text:" + Environment.NewLine + Environment.NewLine +
                full;
        }
    }
}
