using Roof.Engine.Tools;
using Roof.Common.Tools;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Roof.Engine.Managers
{
    internal class SystemResourceManager
    {
        private static readonly TimeSpan _defaultLogFileRotationTime = TimeSpan.FromHours(24);
        private static readonly TimeSpan _defaultLogFileRetentionTime = TimeSpan.FromDays(30);
        private static readonly string[] _linuxServicesToDisable = { "cron", "thd" };

        private static DateTime _lastRotatedLogFileTime = DateTime.UtcNow;

        public static async void DisableLinuxServices()
        {
            foreach (var service in _linuxServicesToDisable)
            {
                Logger.LogToFile($"[SystemResourceManager]: Disabling {service}...");
                await Managers.SystemResourceManager.DisableServiceAsync(service);
            }
        }

        private static async Task DisableServiceAsync(string serviceName)
        {
            try
            {
                //Logger.Log($"Executing: sudo systemctl disable {serviceName}");
                string disableServiceResult = await Shell.ExecuteAsync($"systemctl disable {serviceName}");
                //Logger.Log($"disableServiceResult: {disableServiceResult}");

                //Logger.Log($"Executing: sudo systemctl is-active {serviceName}");
                string isServiceActive = await Shell.ExecuteAsync($"systemctl is-active {serviceName}");
                //Logger.Log($"isServiceActive: {isServiceActive}");

                //Logger.Log($"Executing: sudo systemctl is-enabled {serviceName}");
                string isServiceEnabled = await Shell.ExecuteAsync($"systemctl is-enabled {serviceName}");
                //Logger.Log($"isServiceEnabled: {isServiceEnabled}");

                if (disableServiceResult.Contains("Failed to disable unit"))
                {
                    Logger.LogToFile($"[SystemResourceManager] - DisableServiceAsync(): Service {serviceName} does not exist. NativeResult: {disableServiceResult}", LogSeverity.Warning);
                    return;
                }

                if (isServiceActive.Contains("inactive") && isServiceEnabled.Contains("disabled"))
                {
                    Logger.LogToFile($"[SystemResourceManager]: Successfully disabled {serviceName}.");
                }
                else
                {
                    Logger.LogToFile($"[SystemResourceManager] - DisableServiceAsync(): Failed to disable {serviceName}. NativeResult: {disableServiceResult}", LogSeverity.Danger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[SystemResourceManager] - DisableServiceAsync(): Exception occurred while disabling {serviceName}.", ex);
            }
        }

        public static async Task MonitorAndRotateLogFile()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                if (currentTime - _lastRotatedLogFileTime >= _defaultLogFileRotationTime)
                {
                    Logger.LogToFile("[SystemResourceManager]: Rotating log file...");

                    //Cache the current log file path before rotation.
                    string oldLogFilePath = SafePath.CanonicalizeUnder(Logger.LogFilePath, Path.GetDirectoryName(Logger.LogFilePath)!);

                    Logger.RotateLogFile();
                    Logger.LogToFile("[SystemResourceManager]: Log rotation completed.");
                    _lastRotatedLogFileTime = currentTime;

                    //This now will be logged in the new rotated log file.
                    Logger.LogToFile("[SystemResourceManager]: Compressing old log file...");
                    
                    string zstdCompressionResult = await Shell.ExecuteAsync($"zstd -3 -q --rm -- {SafePath.ShellQuote(oldLogFilePath)}");
                    if (!string.IsNullOrEmpty(zstdCompressionResult))
                        Logger.LogToFile($"[SystemResourceManager]: Failed to compress old log file. NativeResult: {zstdCompressionResult}", LogSeverity.Danger);
                    else
                        Logger.LogToFile("[SystemResourceManager]: Log compression completed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[SystemResourceManager]: Exception occurred while monitoring and rotating log file.", ex);
            }
        }

        public static void MonitorAndEvictLogFiles()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var logFiles = Directory.GetFiles(Logger.LogFileDirectoryPath, "*.log.zst");

                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if ((currentTime - fileInfo.LastWriteTimeUtc) < _defaultLogFileRetentionTime)
                        continue;

                    try
                    {
                        File.Delete(logFile);
                        Logger.LogToFile($"[SystemResourceManager]: Evicted expired log file {logFile}.");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogExceptionToFile($"[SystemResourceManager]: Failed to evict log file {logFile}.",ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[SystemResourceManager]: Exception occurred while monitoring and evicting log files.", ex);
            }
        }
    }
}
