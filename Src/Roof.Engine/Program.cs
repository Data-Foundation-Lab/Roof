using Roof.Engine.Services;
using Roof.Engine.Tools;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Roof.Common.Networking;
using Roof.Engine.Managers;
using Roof.Common.Tools;

namespace Roof.Engine
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var signals = ShutdownManager.RegisterSignals();
            try
            {
                Logger.Log($"VERSION - {Version.Get()} ({Version.GetCompileFlag()})", LogSeverity.Warning);

#if !DEBUG
                Logger.LogToFile($"VERSION - {Version.Get()} ({Version.GetCompileFlag()})", LogSeverity.Warning);
#endif
                Logger.LogToFile("[Program]: Engine starting...");
                await PerformSystemChecks();

                ShutdownManager.Token.ThrowIfCancellationRequested();

                var initStartServicesResult = await InitializeAndStartServices();
                if (initStartServicesResult)
                {
                    Logger.LogToFile("[Program]: Engine startup completed.");
                    Logger.LogToFile("Entering main loop.");
                    await Task.Delay(Timeout.Infinite, ShutdownManager.Token);
                }
                else
                {
                    Logger.LogToFile("[Program]: Engine startup failed.", LogSeverity.Danger);
                }
            }
            catch (OperationCanceledException) when (ShutdownManager.Token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                Logger.LogExceptionToFile("[Program]: Critical engine failure.", ex);
            }
            finally
            {
                await ShutdownManager.Shutdown();
            }
        }

        private static async Task<bool> InitializeAndStartServices()
        {
            Logger.LogToFile("Initializing Configuration Manager...");
            bool configInitStatus = await ConfigurationManager.Initialize();

            if (!configInitStatus)
            {
                Logger.LogToFile("Failed to initialize Configuration Manager.", LogSeverity.Danger);
                return false;
            }
          
//#if RELEASE ||  BETA || IA || DEBUG
#if RELEASE ||  BETA || IA
            var conf = ConfigurationManager.GetConfiguration();
            if (!IsInternetAvailable())
            {
                if(!conf.HasCompletedInitialSetup)
                    Logger.LogToFile("Initial setup mode detected. Creating local setup AP network...");
                else
                    Logger.LogToFile("No internet connection detected. Creating local setup AP network...");
                
                bool setupAPResult = await SetupAPManager.CreateSetupAP();
                if (!setupAPResult)
                {
                    Logger.LogToFile(
                        "Failed to create local setup AP network. Initial setup is only allowed through direct connection.",
                        LogSeverity.Danger);
                }
                else
                {
                    Logger.LogToFile("Starting local setup AP network...");
                    
                    bool setupAPStartResult = await SetupAPManager.StartSetupAP();
                    if (!setupAPStartResult)
                    {
                        Logger.LogToFile(
                            "Failed to start local setup AP network. Initial setup is only allowed through direct connection.",
                            LogSeverity.Danger);
                    }
                    else
                    {
                        Logger.LogToFile("Successfully started local setup AP network. Initial setup is allowed through both direct and AP connections.");
                    }
                }
            }
#endif
            
            // Shared storage and SFTP dependencies are required even on a fresh install.
            // Finish provisioning before any client requests can be accepted.
            Logger.LogToFile("Initializing shared storage and SFTP prerequisites...");
            try
            {
                // TEMPORARY: Remove this call after all existing playground migrations are completed.
                if (ConfigurationManager.GetConfiguration().RegisteredClients.Values.Any(c => !string.IsNullOrEmpty(c.OSSystemUsername)))
                    ClientFileSystemManager.MigrateLegacyPlaygrounds();

                await ClientFileSystemManager.EnsureSharedPlayground();
                string restartResult = await Shell.ExecuteAsync("sudo systemctl restart ssh");
                if (!string.IsNullOrEmpty(restartResult))
                    throw new IOException(restartResult);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[Program]: Failed to initialize shared playground.", ex);
                return false;
            }

            ShutdownManager.Token.ThrowIfCancellationRequested();

            Logger.LogToFile("Initializing Services...");
            await ServiceManager.InitializeServices();

            ShutdownManager.Token.ThrowIfCancellationRequested();

            Logger.LogToFile("Starting Services...");
            ServiceManager.StartServices();

            return true;
        }

        private static async Task PerformSystemChecks()
        {
            Logger.LogToFile("Performing system checks...");

            // Security and Integrity Check.... (+Firewall Check)
            await SecurityManager.PeformSecurityAndIntegrityCheck();
            ShutdownManager.Token.ThrowIfCancellationRequested();
            
            // Check for I/O devices...
            await DeviceManager.PerformDeviceCheck();
            ShutdownManager.Token.ThrowIfCancellationRequested();

            Logger.LogToFile("Checking for internet connection...");
            if (IsInternetAvailable())
            {
#if RELEASE || BETA || IA
//#if DEBUG
                Logger.LogToFile("Internet connection is available. Checking for updates...");

                bool updateCheckStatus = await UpdateManager.CheckForUpdate();
                if (!updateCheckStatus)
                {
                    Logger.LogToFile("Failed to perform update check. CAUTION: Engine may be running on an outdated version.", LogSeverity.Warning);
                }
#endif
            }
            else
            {
                Logger.LogToFile("Internet connection is not available. Skipping update check. Checking for local clients...");
            }

            ShutdownManager.Token.ThrowIfCancellationRequested();
            Logger.LogToFile("Checking for local SFTP server availability...");
            await EnsureSftpServerIsConfigured();

#if RELEASE || BETA || IA
            Logger.LogToFile("Checking startup service availability...");
            await CreateStartupService();
#endif
        }

        private static async Task EnsureSftpServerIsConfigured()
        {
            var openSSHCheckRes = await Shell.ExecuteAsync($"dpkg -L openssh-server | grep bin/");
            if (!string.IsNullOrEmpty(openSSHCheckRes))
            {
                await CheckAndConfigureSshService();
            }
            else
            {
                Logger.LogToFile("Local SSH server is not available. Installing and configuring local SSH server...");
                await InstallOpenSSHServer();
            }
        }

        private static async Task CheckAndConfigureSshService()
        {
            var sshStatusRes = await Shell.ExecuteAsync($"systemctl is-active ssh");
            if (sshStatusRes == "active")
            {
                Logger.LogToFile("Local SSH server is active. Ensuring SFTP configuration...");
                await EnsureSftpConfiguration();
            }
            else
            {
                Logger.LogToFile("Local SSH server is not active. Installing and configuring local SSH server...");
                await InstallOpenSSHServer();
            }
        }

        private static async Task EnsureSftpConfiguration()
        {
            const string sshdConfigFile = "/etc/ssh/sshd_config"; //local const
            const string SftpSubsystemEntry = "Subsystem sftp /usr/lib/openssh/sftp-server"; //local const

            var sshdConfig = await File.ReadAllLinesAsync(sshdConfigFile);
            var sftpLine = sshdConfig.FirstOrDefault(line => Regex.IsMatch(line, @"^\s*Subsystem\s+sftp\s+/usr/lib/openssh/sftp-server\s*$", RegexOptions.IgnoreCase));

            //If sftp is not found in the sshd_config
            if (sftpLine == null)
            {
                Logger.LogToFile("Subsystem sftp is not found in the sshd_config. Adding...");
                await File.AppendAllTextAsync(sshdConfigFile, $"{Environment.NewLine}{SftpSubsystemEntry}{Environment.NewLine}");
            }
            //If sftp is found in the sshd_config, but is commented out
            else if (sftpLine.TrimStart().StartsWith("#"))
            {
                Logger.LogToFile("Subsystem sftp is commented out. Uncommenting...");
                var updatedConfig = sshdConfig.Select(line => line.Contains("Subsystem       sftp    /usr/lib/openssh/sftp-server") && line.TrimStart().StartsWith("#")
                                                            ? SftpSubsystemEntry
                                                            : line).ToList();
                await File.WriteAllLinesAsync(sshdConfigFile, updatedConfig);
            }
            //Sftp is enabled in the sshd_config
            else
            {
                Logger.LogToFile("Subsystem sftp is already configured correctly.");
            }
        }

        private static async Task InstallOpenSSHServer()
        {
            var openSSHInstallRes = await Shell.ExecuteAsync($"apt-get install openssh-server -y", 30);
            Logger.LogToFile(openSSHInstallRes);

            var openSSHStartRes = await Shell.ExecuteAsync($"systemctl start sshd");

            if (openSSHStartRes == string.Empty)
            {
                Logger.LogToFile("Local SSH server has been started successfully.");
                await EnsureServiceIsActiveAndEnabled("sshd");
            }
            else
            {
                Logger.LogToFile("Error: Local SSH server could not be started.", LogSeverity.Danger);
            }
        }

        private static async Task EnsureServiceIsActiveAndEnabled(string serviceName)
        {
            string isActiveRes = await Shell.ExecuteAsync($"systemctl is-active {serviceName}");
            if (isActiveRes == "active")
            {
                string isEnabledRes = await Shell.ExecuteAsync($"systemctl is-enabled {serviceName}");
                Logger.LogToFile(isEnabledRes == "enabled"
                    ? "Local SSH server has been installed and configured successfully."
                    : $"Local SSH server could not be enabled. Details: {isEnabledRes}");
            }
            else
            {
                Logger.LogToFile($"Local SSH server is not active after installation.", LogSeverity.Danger);
            }
        }

        private static async Task CreateStartupService()
        {
            const string UNIT = "roofenginestartup.service";
            string path = STARTUP_SERVICE_FILE_PATH;
            Logger.LogToFile(File.Exists(path) ? "Startup system service exists." : "Creating startup system service...");

            // If it already exists, check whether if it is enabled.
            if (File.Exists(path))
            {
                string status = await Shell.ExecuteAsync($"systemctl is-enabled {UNIT}");
                if (status.Trim() != "enabled")
                {
                    Logger.LogToFile("Startup system service is not enabled. Enabling startup system service...");
                    
                    string enableOut = await Shell.ExecuteAsync($"sudo systemctl enable {UNIT}");
                    if (!string.IsNullOrEmpty(enableOut))
                    {
                        Logger.LogToFile($"Failed to enable startup system service. {enableOut}", LogSeverity.Danger);
                        return;
                    }
                    else
                    {
                        Logger.LogToFile("Successfully enabled startup system service.");
                    }
                }
                
                Logger.LogToFile("Startup system service is available.");
                return;
            }

            try
            {
                // 0) Unmask first (idempotent). Log output as info, not failure.
                string unmaskOut = await Shell.ExecuteAsync($"sudo systemctl --quiet unmask {UNIT} || true");
                if (!string.IsNullOrWhiteSpace(unmaskOut))
                    Logger.LogToFile($"Unmasked startup system service. NOTE: {unmaskOut.Trim()}");

                // 1) Ensure no leftover entry at the target path (symlink or stale file)
                string rmOut = await Shell.ExecuteAsync($"sudo rm -f {path}");
                if (!string.IsNullOrWhiteSpace(rmOut))
                    Logger.LogToFile(rmOut.Trim());

                // 2) Build the unit file content
        #if DEBUG
                string unitText = DEBUG_STARTUP_SERVICE_FILE_CONTENT;
        #endif
        #if RELEASE || BETA || IA
                string unitText = RELEASE_IA_STARTUP_SERVICE_FILE_CONTENT(
                    Environment.ProcessPath,
                    Directory.GetCurrentDirectory());
        #endif

                // 3) Write to /tmp and install atomically with correct perms/owner
                string tmp = $"/tmp/{UNIT}.tmp";
                await File.WriteAllTextAsync(tmp, unitText, Encoding.UTF8);

                string installOut = await Shell.ExecuteAsync(
                    $"sudo install -o root -g root -m 0644 {tmp} {path}");
                if (!string.IsNullOrEmpty(installOut))
                    Logger.LogToFile($"Install output: {installOut.Trim()}");

                // 4) Reload systemd
                string reloadOut = await Shell.ExecuteAsync("sudo systemctl daemon-reload");
                if (!string.IsNullOrEmpty(reloadOut))
                {
                    Logger.LogToFile($"Failed to reload system service daemon. {reloadOut}", LogSeverity.Danger);
                    return;
                }

                // 5) Enable
                string enableOut = await Shell.ExecuteAsync($"sudo systemctl enable {UNIT}");
                if (!string.IsNullOrEmpty(enableOut) &&
                    !enableOut.Trim().Contains("Created symlink", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"Failed to enable startup system service. {enableOut}", LogSeverity.Danger);
                    return;
                }

                // 6) Start || Don't start it. It will run another instance.
                /*string startOut = await Shell.ExecuteAsync($"sudo systemctl start {UNIT}");
                if (!string.IsNullOrEmpty(startOut) &&
                    startOut.Trim().StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"Failed to start startup system service. {startOut}", LogSeverity.Danger);
                    return;
                }
                else if (!string.IsNullOrWhiteSpace(startOut))
                {
                    Logger.LogToFile(startOut.Trim());
                }*/

                Logger.LogToFile("Successfully created startup system service.");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("Failed to create startup system service.", ex);
            }
        }

        private const string STARTUP_SERVICE_FILE_PATH = "/etc/systemd/system/roofenginestartup.service";
        
        private const string DEBUG_STARTUP_SERVICE_FILE_CONTENT =
            "[Unit]\n" +
            "Description=Roof Engine Startup Service\n" +
            "After=network-online.target local-fs.target\n" +
            "Wants=network-online.target\n" +
            //If /roof is a separate mount or a bind/volume, ensure it’s ready:
            "RequiresMountsFor=/roof\n" +
            //Safety checks (fail fast if missing):
            "ConditionPathIsDirectory=/roof\n" +
            "ConditionPathExists=/roof/Roof.Engine\n" +
            "\n" +
            "[Service]\n" +
            "Type=simple\n" +
            "WorkingDirectory=/roof\n" +
            "ExecStart=/roof/Roof.Engine\n" +
            "Restart=on-failure\n" +
            //For debugging:
            "StandardOutput=journal\n" +
            "StandardError=journal\n" +
            "\n" +
            "[Install]\n" +
            "WantedBy=multi-user.target\n";

        private static string RELEASE_IA_STARTUP_SERVICE_FILE_CONTENT(string fullPathToBinary, string workingDirPath)
        {
            return "[Unit]\n" +
                   "Description=Roof Engine Startup Service\n" +
                   "After=network-online.target local-fs.target\n" +
                   "Wants=network-online.target\n" +
                   "\n" +
                   "[Service]\n" +
                   "Type=oneshot\n" +
                   "RemainAfterExit=yes\n" +
                   // Don't track/kill children when the unit stops
                   "KillMode=none\n" +
                   // The service itself should not restart
                   "Restart=no\n" +
                   $"WorkingDirectory={workingDirPath}\n" +
                   "\n" +
                   // Fully detach:
                   // - setsid -f: new session + fork (daemonize)
                   // - nohup & background: survive SIGHUP; redirect stdio
                   // - exec: replace shell so signals don't hit an intermediate
                   $"ExecStart=/usr/bin/env bash -lc 'exec setsid -f {fullPathToBinary} </dev/null >/dev/null 2>&1 &'\n" +
                   "\n" +
                   "[Install]\n" +
                   "WantedBy=multi-user.target";
        }

        internal static bool IsInternetAvailable()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 3000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}