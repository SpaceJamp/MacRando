using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace MacRando
{
    internal sealed class PowerShellCommandException : Exception
    {
        public PowerShellCommandException(string message)
            : base(message)
        {
        }
    }

    internal sealed class PowerShellResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
    }

    internal static class PowerShellRunner
    {
        public static async Task<PowerShellResult> RunAsync(
            string script,
            IDictionary<string, string> environment,
            int timeoutMilliseconds)
        {
            string powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> item in environment)
                {
                    startInfo.EnvironmentVariables[item.Key] = item.Value ?? string.Empty;
                }
            }

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                if (!process.Start())
                {
                    throw new InvalidOperationException("Windows PowerShell could not be started.");
                }

                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();

                bool exited = await Task.Run(() =>
                {
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        bool terminated = KillProcessTree(process);
                        if (!terminated)
                        {
                            try
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                            catch
                            {
                            }
                        }

                        CloseOutputStreams(process);
                        return false;
                    }

                    // Flush asynchronous output streams after the process exits.
                    process.WaitForExit();
                    return true;
                });

                if (!exited)
                {
                    ObserveFault(standardOutput);
                    ObserveFault(standardError);
                    throw new TimeoutException(
                        "A Windows networking command did not finish within " +
                        (timeoutMilliseconds / 1000) + " seconds.");
                }

                string output;
                string error;
                try
                {
                    output = await ReadWithTimeoutAsync(standardOutput, "standard output", 5000);
                    error = await ReadWithTimeoutAsync(standardError, "standard error", 5000);
                }
                catch
                {
                    CloseOutputStreams(process);
                    ObserveFault(standardOutput);
                    ObserveFault(standardError);
                    throw;
                }

                return new PowerShellResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = output ?? string.Empty,
                    StandardError = error ?? string.Empty
                };
            }
        }

        private static async Task<string> ReadWithTimeoutAsync(
            Task<string> task,
            string streamName,
            int timeoutMilliseconds)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
            if (completed != task)
            {
                throw new TimeoutException("The Windows PowerShell " + streamName + " stream did not close.");
            }

            return await task;
        }

        private static bool KillProcessTree(Process process)
        {
            int processId;
            try
            {
                processId = process.Id;
            }
            catch
            {
                return false;
            }

            try
            {
                string taskKillPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "taskkill.exe");
                ProcessStartInfo taskKillInfo = new ProcessStartInfo
                {
                    FileName = taskKillPath,
                    Arguments = "/PID " + processId + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process taskKill = new Process())
                {
                    taskKill.StartInfo = taskKillInfo;
                    if (!taskKill.Start())
                    {
                        return KillSingleProcess(process);
                    }

                    taskKill.WaitForExit(3000);
                    if (taskKill.ExitCode != 0 && !HasExited(process))
                    {
                        return KillSingleProcess(process);
                    }
                }
            }
            catch
            {
                return KillSingleProcess(process);
            }

            try
            {
                process.WaitForExit(3000);
                return HasExited(process);
            }
            catch
            {
                return false;
            }
        }

        private static bool KillSingleProcess(Process process)
        {
            try
            {
                if (!HasExited(process))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }

                return HasExited(process);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void CloseOutputStreams(Process process)
        {
            try
            {
                process.StandardOutput.Close();
            }
            catch
            {
            }

            try
            {
                process.StandardError.Close();
            }
            catch
            {
            }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                completed => { AggregateException ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public static async Task<T> RunJsonAsync<T>(
            string script,
            IDictionary<string, string> environment,
            int timeoutMilliseconds)
        {
            string wrappedScript =
                "$ErrorActionPreference = 'Stop'\n" +
                "$ProgressPreference = 'SilentlyContinue'\n" +
                "function Write-MacRandoJson64([object]$Value) {\n" +
                "  $json = ConvertTo-Json -InputObject $Value -Depth 8 -Compress\n" +
                "  [Console]::Out.WriteLine('__MACRANDO_JSON__')\n" +
                "  [Console]::Out.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))\n" +
                "}\n" +
                script;

            PowerShellResult result = await RunAsync(wrappedScript, environment, timeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw CreateCommandException(result);
            }

            string[] outputLines = result.StandardOutput.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            int markerIndex = -1;
            for (int index = outputLines.Length - 1; index >= 0; index--)
            {
                if (string.Equals(outputLines[index].Trim(), "__MACRANDO_JSON__", StringComparison.Ordinal))
                {
                    markerIndex = index;
                    break;
                }
            }

            string encoded = markerIndex >= 0 && markerIndex + 1 < outputLines.Length
                ? outputLines[markerIndex + 1].Trim()
                : (outputLines.Length == 0 ? string.Empty : outputLines[outputLines.Length - 1].Trim());
            if (string.IsNullOrWhiteSpace(encoded))
            {
                throw new PowerShellCommandException(
                    "Windows PowerShell returned no data. " + CleanError(result.StandardError));
            }

            try
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                T value = new JavaScriptSerializer().Deserialize<T>(json);
                if (value == null)
                {
                    throw new InvalidOperationException("Windows PowerShell returned an empty result.");
                }

                return value;
            }
            catch (Exception error)
            {
                throw new PowerShellCommandException(
                    "MacRando could not decode the Windows networking result: " + error.Message);
            }
        }

        public static PowerShellCommandException CreateCommandException(PowerShellResult result)
        {
            string detail = CleanError(result.StandardError);
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = CleanError(result.StandardOutput);
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = "PowerShell exit code " + result.ExitCode + ".";
            }

            return new PowerShellCommandException(detail);
        }

        private static string CleanError(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                if (value.IndexOf("<S ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var document = new XmlDocument();
                    document.LoadXml(value);
                    XmlNodeList nodes = document.GetElementsByTagName("S");
                    string best = string.Empty;
                    foreach (XmlNode node in nodes)
                    {
                        string text = Regex.Replace(node.InnerText ?? string.Empty, @"\s+", " ").Trim();
                        if (text.Length > best.Length)
                        {
                            best = text;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(best))
                    {
                        return best;
                    }
                }
            }
            catch
            {
                // Fall back to removing XML markup when the complete CLIXML document is not available.
            }

            string cleaned = Regex.Replace(value, "<[^>]+>", " ");
            cleaned = cleaned.Replace("\\u000D", " ").Replace("\\u000A", " ");
            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }
    }
}
