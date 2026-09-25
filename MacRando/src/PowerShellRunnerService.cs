using System.Collections.Generic;
using System.Threading.Tasks;

namespace MacRando
{
    internal interface IPowerShellRunner
    {
        Task<PowerShellResult> RunAsync(string script, IDictionary<string, string> environment, int timeoutMilliseconds);
        Task<T> RunJsonAsync<T>(string script, IDictionary<string, string> environment, int timeoutMilliseconds);
    }

    internal sealed class WindowsPowerShellRunner : IPowerShellRunner
    {
        public Task<PowerShellResult> RunAsync(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            return PowerShellRunner.RunAsync(script, environment, timeoutMilliseconds);
        }

        public Task<T> RunJsonAsync<T>(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            return PowerShellRunner.RunJsonAsync<T>(script, environment, timeoutMilliseconds);
        }
    }
}
