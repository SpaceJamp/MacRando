using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MacRando
{
    internal static class LicenseInfo
    {
        public const string SpdxId = "Apache-2.0";
        public const string Name = "Apache License, Version 2.0";
        public const string Url = "https://www.apache.org/licenses/LICENSE-2.0";
        public const string Copyright = "Copyright (c) 2026 SpaceJamp";
        public const string ResourceName = "MacRando.LICENSE.txt";

        public static string Notice
        {
            get { return Copyright + "  •  License: " + SpdxId; }
        }

        public static string Summary
        {
            get
            {
                return Copyright + Environment.NewLine +
                    "Licensed under the " + Name + " (" + SpdxId + ")." + Environment.NewLine +
                    Environment.NewLine +
                    "You may use, modify, and redistribute this software, including in larger works, " +
                    "provided that recipients receive a copy of the license, modified files carry " +
                    "prominent change notices, and existing attribution notices are retained." + Environment.NewLine +
                    Environment.NewLine +
                    "This software is provided on an AS IS basis, without warranties or conditions " +
                    "of any kind. MacRando changes live network adapter settings and requires " +
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
                "Licensed under the " + Name + " (" + SpdxId + ")." +
                Environment.NewLine + Environment.NewLine +
                "Full license text:" + Environment.NewLine + Environment.NewLine +
                full;
        }
    }
}
