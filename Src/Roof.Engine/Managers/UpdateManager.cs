using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Roof.Common.Tools;
using Roof.Engine.Tools;

namespace Roof.Engine.Managers
{
    public class UpdateManager
    {
        public static async Task<bool> CheckForUpdate()
        {
            try
            {
                if (ShouldReplaceBinary())
                {
                    if (!await ReplaceBinaryAsync())
                    {
                        return false;
                    }
                }
                
                string? latestVer = await ReleaseServerIngressManager.GetLatestVersion();
                if (latestVer == null)
                {
                    Logger.LogToFile("[UpdateManager]: Failed to get latest version.", LogSeverity.Danger);
                    return false;
                }

                string? engineOfficialHash = await ReleaseServerIngressManager.GetOfficialEngineHash();
                if (engineOfficialHash == null)
                {
                    Logger.LogToFile("[UpdateManager]: Failed to get latest engine hash.", LogSeverity.Danger);
                    return false;
                }
                
                bool currentBinaryHashCheckStatus = await VerifyCurrentBinaryHash(engineOfficialHash);
                
                if (Version.Get().Equals(latestVer) && currentBinaryHashCheckStatus)
                {
                    Logger.LogToFile("[UpdateManager]: No update found.");
                    return true;
                }
                
                if(Version.Get().Equals(latestVer) == false)
                    Logger.LogToFile($"[UpdateManager]: Version mismatch detected. Latest official version is: {latestVer}");
                
                if(currentBinaryHashCheckStatus == false)
                    Logger.LogToFile($"[UpdateManager]: Hash mismatch detected. Latest official hash is: {engineOfficialHash}");
                
                Logger.LogToFile("[UpdateManager]: Downloading binary...");

                bool downloadNewHashBinaryResult = await DownloadLatestCurrentBinary(latestVer, engineOfficialHash);
                if (!downloadNewHashBinaryResult)
                {
                    Logger.LogToFile("[UpdateManager]: Failed to download the latest binary.", LogSeverity.Danger);
                    return false;
                }

                Logger.LogToFile("[UpdateManager]: Successfully downloaded the latest version. Restarting...");
                await Task.Delay(5000);
                    
                await RestartApplication(latestVer, engineOfficialHash);
                ShutdownManager.RequestShutdown();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Exception occurred in CheckForUpdate().", ex);
                return false;
            }

            return false;
        }
        
        private static bool ShouldReplaceBinary()
        {
            var procPath = Environment.ProcessPath ?? string.Empty;

            //Logger.Log($"ShouldReplaceBinary(): {procPath}");

            var procName = Path.GetFileName(procPath);
            bool isExpected = string.Equals(procName, "Roof.Engine", StringComparison.OrdinalIgnoreCase);

            return !isExpected;
        }
        
        private static async Task<bool> ReplaceBinaryAsync()
        {
            try
            {
#if RELEASE || BETA || IA || DEBUG
                string currentDir = Directory.GetCurrentDirectory();
                if (OperatingSystem.IsLinux())
                {
                    string target = SafePath.CanonicalizeUnder(Path.Combine(currentDir, "Roof.Engine"), currentDir);
                    string source = SafePath.CanonicalizeUnder(Path.Combine(currentDir, $"Roof.Engine_{Version.Get()}"), currentDir);
                    string? engineOfficialHash = await ReleaseServerIngressManager.GetOfficialEngineHash();
                    if (engineOfficialHash == null)
                    {
                        Logger.LogToFile("[UpdateManager]: Failed to get latest engine hash before binary replacement.", LogSeverity.Danger);
                        return false;
                    }

                    if (!await VerifyDownloadedArtifactHash(source, engineOfficialHash))
                        return false;

                    Logger.LogToFile("[UpdateManager]: Replacing Engine binary...");
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(source, target);
                    Logger.LogToFile("[UpdateManager]: Engine binary replacement completed.");
                }
                else
                {
                    Logger.LogToFile("[UpdateManager]: UNSUPPORTED PLATFORM for binary replacement.", LogSeverity.Danger);
                    return false;
                }
#endif
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Failed to finalize update process.", ex);
                return false;
            }
        }
        
        private static async Task<bool> VerifyCurrentBinaryHash(string expectedHash)
        {
            try
            {
                try
                {
                    using var fs = new FileStream(
                        Environment.ProcessPath!, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 8192, useAsync: true);

                    using var ms = new MemoryStream();
                    await fs.CopyToAsync(ms);
                    var bytes = ms.ToArray();

                    var computedHash = Security.SHA256.ComputeSha256Hash(bytes);
                    return expectedHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase);
                }
                catch (FileNotFoundException) { /* try next */ }
                catch (DirectoryNotFoundException) { /* try next */ }
                // let other exceptions (e.g., access) bubble to outer catch for logging

                Logger.LogToFile("[UpdateManager]: VerifyCurrentBinaryHash(): no readable candidate file found.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Exception occurred in VerifyCurrentBinaryHash().", ex);
                return false;
            }
        }
        
        private static async Task RestartApplication(string latestVer, string expectedHash)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    // On Linux the currently running binary is locked.
                    // Use a bash command to wait for a moment (allowing the current process to exit),
                    // then set permissions, move the new binary into place, and launch it.
                    
                    if (!SecurityManager.IsValidFilename($"Roof.Engine_{latestVer}"))
                    {
                        Logger.LogToFile("[UpdateManager]: Rejected invalid update filename.", LogSeverity.Danger);
                        return;
                    }

                    string newExecutable = SafePath.CanonicalizeUnder(Path.Combine(Directory.GetCurrentDirectory(), $"Roof.Engine_{latestVer}"), Directory.GetCurrentDirectory());
                    string targetExecutable = SafePath.CanonicalizeUnder(Path.Combine(Directory.GetCurrentDirectory(), "Roof.Engine"), Directory.GetCurrentDirectory());

                    if (!await VerifyDownloadedArtifactHash(newExecutable, expectedHash))
                        return;

#if DEBUG
                    ReplaceAndExec(newExecutable, targetExecutable);
#elif RELEASE || BETA || IA
                    string command = $"(sleep 2; printf '%s  %s\\n' {SafePath.ShellQuote(expectedHash)} {SafePath.ShellQuote(newExecutable)} | sha256sum -c --status && chmod 755 -- {SafePath.ShellQuote(newExecutable)} && mv -- {SafePath.ShellQuote(newExecutable)} {SafePath.ShellQuote(targetExecutable)} && sudo -n -- {SafePath.ShellQuote(targetExecutable)} >/dev/null 2>&1 &) &";
                    var startInfo = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
                    startInfo.ArgumentList.Add("-c");
                    startInfo.ArgumentList.Add(command);
                    Process.Start(startInfo);
#endif
                    return;
                }
                else
                {
                    Logger.LogToFile("[UpdateManager]: UNSUPPORTED PLATFORM for restart.", LogSeverity.Danger);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Exception occurred in RestartApplication().", ex);
                return;
            }
        }
        
        private static async Task<bool> DownloadLatestCurrentBinary(string latestVersion, string expectedHash)
        {
            string? filePath = null;

            try
            {
                using HttpClient httpClient = new HttpClient();
                
                // Get download URL and file extension.
                var (downloadUrl, appExtension) = ReleaseServerIngressManager.GetEngineDownloadInfo(latestVersion);

                // Build the file path.
                string fileName = $"Roof.Engine_{latestVersion}{appExtension}";
                if (!SecurityManager.IsValidFilename(fileName))
                {
                    Logger.LogToFile("[UpdateManager]: Rejected invalid update filename.", LogSeverity.Danger);
                    return false;
                }
                filePath = SafePath.CanonicalizeUnder(Path.Combine(Directory.GetCurrentDirectory(), fileName), Directory.GetCurrentDirectory());

                // Start the HTTP request and set the option to read headers first.
                using HttpResponseMessage response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // Get the total file size (if available).
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                bool canReportProgress = totalBytes != -1;

                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;
                    int lastReportedPercent = 0;

                    // Read the content stream in a loop.
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            int currentProgressPercent = (int)(totalRead * 100 / totalBytes);
                            if (currentProgressPercent > lastReportedPercent)
                            {
                                lastReportedPercent = currentProgressPercent;
                                //Logger.Log($"[UpdateManager]: {currentProgressPercent:F2}% downloaded");
                            }
                        }
                    }
                }

                return await VerifyDownloadedArtifactHash(filePath, expectedHash);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Exception occurred in DownloadLatestCurrentBinary().", ex);
                DeleteInvalidDownloadedArtifact(filePath);
                return false;
            }
        }

        private static async Task<bool> VerifyDownloadedArtifactHash(string filePath, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.LogToFile("[UpdateManager]: CRITICAL UPDATE INTEGRITY FAILURE: Downloaded artifact was missing before hash verification.", LogSeverity.Danger);
                return false;
            }

            string computedHash = await ComputeFileSha256Hash(filePath);
            if (expectedHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogToFile("[UpdateManager]: Downloaded binary hash matches official metadata.");
                return true;
            }

            Logger.LogToFile(
                $"[UpdateManager]: CRITICAL UPDATE INTEGRITY FAILURE: Downloaded binary hash mismatch. Expected: {expectedHash}. Actual: {computedHash}. Artifact: {filePath}",
                LogSeverity.Danger);

            DeleteInvalidDownloadedArtifact(filePath);
            return false;
        }

        private static async Task<string> ComputeFileSha256Hash(string filePath)
        {
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                useAsync: true);

            byte[] hash = await SHA256.HashDataAsync(fileStream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void DeleteInvalidDownloadedArtifact(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                File.Delete(filePath);
                Logger.LogToFile($"[UpdateManager]: Deleted invalid downloaded artifact: {filePath}", LogSeverity.Danger);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[UpdateManager]: Failed to delete invalid downloaded artifact: {filePath}", ex);
            }
        }
        
#if DEBUG
        [DllImport("libc", SetLastError = true)]
        private static extern int execv(IntPtr path, IntPtr argv);

        // helper to build argv array for execv
        private static IntPtr BuildArgv(params string[] args)
        {
            // allocate array of pointers (argc + 1) and copy strings as ANSI
            var ptrs = new IntPtr[args.Length + 1];
            for (int i = 0; i < args.Length; i++)
            {
                ptrs[i] = Marshal.StringToHGlobalAnsi(args[i]);
            }
            ptrs[args.Length] = IntPtr.Zero; // terminating null

            IntPtr argv = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Length);
            for (int i = 0; i < ptrs.Length; i++)
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, ptrs[i]);
            return argv;
        }

        // free argv memory (call only if execv fails)
        private static void FreeArgv(IntPtr argv, int argc)
        {
            for (int i = 0; i < argc; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
            Marshal.FreeHGlobal(argv);
        }

        private static void ReplaceAndExec(string newExecutable, string targetExecutable)
        {
            // sanity checks
            if (!File.Exists(newExecutable))
                throw new FileNotFoundException("New executable not found", newExecutable);

            // make executable
            newExecutable = SafePath.CanonicalizeUnder(newExecutable, Directory.GetCurrentDirectory());
            targetExecutable = SafePath.CanonicalizeUnder(targetExecutable, Directory.GetCurrentDirectory());
            var chmodInfo = new ProcessStartInfo("chmod") { UseShellExecute = false };
            chmodInfo.ArgumentList.Add("755");
            chmodInfo.ArgumentList.Add("--");
            chmodInfo.ArgumentList.Add(newExecutable);
            using var chmod = Process.Start(chmodInfo)!;
            chmod.WaitForExit();
            if (chmod.ExitCode != 0)
                throw new Exception("chmod failed");

            // move/replace (atomic on same filesystem)
            File.Move(newExecutable, targetExecutable, overwrite: true);

            // small sleep optional if you want a brief pause
            Thread.Sleep(200);

            // prepare argv (argv[0] is path)
            IntPtr argv = BuildArgv(targetExecutable); // no extra arguments; add more in array if needed

            // execv: replace current process
            int res = execv(Marshal.StringToHGlobalAnsi(targetExecutable), argv);

            // if execv returns, it failed — clean up and throw with errno
            int err = Marshal.GetLastWin32Error();
            FreeArgv(argv, 1);
            throw new System.ComponentModel.Win32Exception(err, $"execv failed with errno {err}");
        }
#endif
    }
}
