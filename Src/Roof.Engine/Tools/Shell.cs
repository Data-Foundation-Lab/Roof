using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Roof.Common.Tools;

namespace Roof.Engine.Tools
{
    internal class Shell
    {
        public static async Task<string> ExecuteAsync(string command, int timeOut = 10)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));
            try
            {
                //Logger.Log($"Executing command: {command}");

                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    process.StartInfo.ArgumentList.Add("-c");
                    process.StartInfo.ArgumentList.Add(command);
                    process.Start();

                    var outputTask = ReadStreamAsync(process.StandardOutput, cts.Token);
                    var errorTask = ReadStreamAsync(process.StandardError, cts.Token);

                    await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(Timeout.Infinite, cts.Token));

                    if (cts.IsCancellationRequested)
                    {
                        throw new TimeoutException("The operation has timed out.");
                    }

                    var output = await outputTask;
                    var error = await errorTask;
                    await process.WaitForExitAsync(cts.Token);

                    //Logger.Log($"Command output: {output}");
                    //Logger.Log($"Command error: {error}");

                    // If there is an error, return the error message
                    if (!string.IsNullOrEmpty(error))
                    {
                        return $"Error: {error}";
                    }

                    if (process.ExitCode != 0)
                        return $"Error: Command exited with code {process.ExitCode}.";

                    return output;
                }
            }
            catch (TimeoutException ex)
            {
                Logger.LogToFile("[Shell] - ExecuteAsync(): Exception occurred while trying to execute a shell command. Exception: The operation timed out.", LogSeverity.Danger);
                return $"Error: The operation timed out.";
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[Shell] - ExecuteAsync(): Exception occurred while trying to execute a shell command. Exception: {ex.Message}", LogSeverity.Danger);
                return $"Error: {ex.Message}";
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static async Task<string> ReadStreamAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            var result = new StringBuilder();
            var buffer = new char[1024];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var readTask = reader.ReadAsync(buffer, 0, buffer.Length);
                    var completedTask = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, cancellationToken));
                    if (completedTask == readTask)
                    {
                        var charsRead = await readTask;
                        if (charsRead == 0)
                        {
                            break;
                        }
                        result.Append(buffer, 0, charsRead);
                    }
                    else
                    {
                        // The operation has been cancelled.
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[Shell] - ReadStreamAsync(): Exception occurred while trying to read shell stream. Exception: {ex.Message}", LogSeverity.Danger);
            }
            return result.ToString().TrimEnd();
        }
    }
}
