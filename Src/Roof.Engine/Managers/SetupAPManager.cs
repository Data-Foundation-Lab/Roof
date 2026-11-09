using Roof.Common.Tools;
using Roof.Engine.Tools;

namespace Roof.Engine.Managers;

internal class SetupAPManager
{
    private const string SETUP_AP_INTERNAL_CONNECTION_NAME = "RoofAP";
    private const string SETUP_EXTERNAL_AP_SSID = "Roof Setup";
    private const string SETUP_AP_INTERFACE_NAME = "ap0";
    private const string WLAN_INTERFACE_NAME = "wlan0";

    private static bool _wirelessRecoveryAttempted;
    private static bool _missingNmcliReported;

    private static readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    public static async Task<bool> UpdateSetupAPRequirement()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            // Direct-network/container setups may not have NetworkManager installed.
            // Recheck each tick so monitoring resumes if nmcli becomes available later.
            string nmcliCheck = await Shell.ExecuteAsync("command -v nmcli >/dev/null 2>&1");
            if (!string.IsNullOrEmpty(nmcliCheck))
            {
                if (!_missingNmcliReported)
                    Logger.LogToFile("[SetupAPManager]: Setup AP monitoring is unavailable because nmcli was not found. Direct network connections remain available.", LogSeverity.Warning);
                _missingNmcliReported = true;
                return false;
            }
            _missingNmcliReported = false;

            bool setupAPRunning = await IsSetupAPRunning();
            bool internetAvailable = Program.IsInternetAvailable();

            if (setupAPRunning && internetAvailable)
            {
                Logger.LogToFile(
                    "[SetupAPManager] - UpdateSetupAPRequirement(): Internet is available. Stopping setup AP...");

                if (await StopSetupAPCore())
                {
                    Logger.LogToFile(
                        "[SetupAPManager] - UpdateSetupAPRequirement(): Successfully stopped setup AP.");
                }
                else
                {
                    Logger.LogToFile(
                        "[SetupAPManager] - UpdateSetupAPRequirement(): Failed to stop local setup AP network.",
                        LogSeverity.Danger);
                    return false;
                }
            }
            else if (!setupAPRunning && !internetAvailable)
            {
                Logger.LogToFile(
                    "[SetupAPManager] - UpdateSetupAPRequirement(): Internet is not available. Starting setup AP for fallback connection...");

                if (await StartSetupAPCore())
                {
                    Logger.LogToFile(
                        "[SetupAPManager] - UpdateSetupAPRequirement(): Successfully started local setup AP network.");
                }
                else
                {
                    Logger.LogToFile(
                        "[SetupAPManager] - UpdateSetupAPRequirement(): Failed to start local setup AP network.",
                        LogSeverity.Danger);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public static async Task<bool> IsSetupAPRunning()
    {
        try
        {
            string result = await Shell.ExecuteAsync(
                "LC_ALL=C nmcli -t -f NAME,TYPE,DEVICE,STATE connection show --active");

            if (result.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - IsSetupAPRunning(): Failed to check setup AP. NativeResult: {result}", LogSeverity.Danger);
                return false;
            }

            string activeConnection = $"{SETUP_AP_INTERNAL_CONNECTION_NAME}:802-11-wireless:{SETUP_AP_INTERFACE_NAME}:activated";
            foreach (string connection in result.Split('\n'))
            {
                if (connection.Trim() == activeConnection)
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - IsSetupAPRunning(): Exception occurred.", ex);
            return false;
        }
    }

    public static async Task<bool> CreateSetupAP()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            if (await IsWirelessReady() && await EnsureAp0InterfaceExists() && await CreateSetupAPProfile())
                return true;

            if (_wirelessRecoveryAttempted)
            {
                Logger.LogToFile("[SetupAPManager] - CreateSetupAP(): Wireless recovery already attempted.", LogSeverity.Warning);
                return false;
            }

            if (!await DoesInterfaceExist(WLAN_INTERFACE_NAME))
            {
                Logger.LogToFile("[SetupAPManager] - CreateSetupAP(): wlan0 is not available.", LogSeverity.Warning);
                return false;
            }

            _wirelessRecoveryAttempted = true;
            Logger.LogToFile("[SetupAPManager] - CreateSetupAP(): Attempting one-time wireless recovery.");
            return await RecoverWireless();
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - CreateSetupAP(): Exception occurred.", ex);
            return false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static async Task<bool> CreateSetupAPProfile()
    {
        try
        {
            Logger.LogToFile("[SetupAPManager] - CreateSetupAP(): Creating setup AP...");
            string profilesResult = await Shell.ExecuteAsync("nmcli -g NAME connection show");
            if (profilesResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to list connection profiles. NativeResult: {profilesResult}", LogSeverity.Danger);
                return false;
            }

            bool profileExists = false;
            foreach (string name in profilesResult.Split('\n'))
            {
                if (name.Trim() == SETUP_AP_INTERNAL_CONNECTION_NAME)
                {
                    profileExists = true;
                    break;
                }
            }

            if (profileExists)
            {
                string deleteResult = await Shell.ExecuteAsync($"nmcli connection delete {SETUP_AP_INTERNAL_CONNECTION_NAME}");
                if (deleteResult.Contains("Error:"))
                {
                    Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to delete existing setup AP. NativeResult: {deleteResult}", LogSeverity.Danger);
                    return false;
                }
            }

            string addResult = await Shell.ExecuteAsync(
                $"nmcli connection add type wifi ifname {SETUP_AP_INTERFACE_NAME} con-name '{SETUP_AP_INTERNAL_CONNECTION_NAME}' ssid '{SETUP_EXTERNAL_AP_SSID}'");

            if (addResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to create setup AP. NativeResult: {addResult}", LogSeverity.Danger);
                return false;
            }

            string modeResult = await Shell.ExecuteAsync(
                $"nmcli connection modify {SETUP_AP_INTERNAL_CONNECTION_NAME} 802-11-wireless.mode ap");

            string bandResult = await Shell.ExecuteAsync(
                $"nmcli connection modify {SETUP_AP_INTERNAL_CONNECTION_NAME} 802-11-wireless.band bg");

            if (modeResult.Contains("Error:") || bandResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to configure setup AP mode/band. NativeResult: {modeResult} {bandResult}", LogSeverity.Danger);
                return false;
            }

            string sharedResult = await Shell.ExecuteAsync(
                $"nmcli connection modify {SETUP_AP_INTERNAL_CONNECTION_NAME} ipv4.method shared");

            if (sharedResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to enable shared networking. NativeResult: {sharedResult}", LogSeverity.Danger);
                return false;
            }

            string autoConnectResult = await Shell.ExecuteAsync(
                $"nmcli connection modify {SETUP_AP_INTERNAL_CONNECTION_NAME} connection.autoconnect no");

            if (autoConnectResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - CreateSetupAP(): Failed to disable autoconnect. NativeResult: {autoConnectResult}", LogSeverity.Danger);
                return false;
            }

            Logger.LogToFile("[SetupAPManager] - CreateSetupAP(): Setup AP created.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - CreateSetupAP(): Exception occurred.", ex);
            return false;
        }
    }

    public static async Task<bool> StartSetupAP()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            return await StartSetupAPCore();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static async Task<bool> StartSetupAPCore()
    {
        try
        {
            Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Starting setup AP...");
            if (await IsSetupAPRunning())
            {
                _wirelessRecoveryAttempted = false;
                return true;
            }

            if (await ActivateSetupAP())
            {
                _wirelessRecoveryAttempted = false;
                Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Setup AP started.");
                return true;
            }

            // Keep later monitor ticks from repeating a failed recovery indefinitely.
            if (_wirelessRecoveryAttempted)
            {
                Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Wireless recovery already attempted.", LogSeverity.Warning);
                return false;
            }

            if (!await DoesInterfaceExist(WLAN_INTERFACE_NAME))
            {
                Logger.LogToFile("[SetupAPManager] - StartSetupAP(): wlan0 is not available.", LogSeverity.Warning);
                return false;
            }

            _wirelessRecoveryAttempted = true;
            Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Attempting one-time wireless recovery.");

            if (!await RecoverWireless() || !await ActivateSetupAP())
            {
                Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Failed after recovery.", LogSeverity.Danger);
                return false;
            }

            _wirelessRecoveryAttempted = false;
            Logger.LogToFile("[SetupAPManager] - StartSetupAP(): Setup AP started after recovery.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - StartSetupAP(): Exception occurred.", ex);
            return false;
        }
    }

    public static async Task<bool> StopSetupAP()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            return await StopSetupAPCore();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static async Task<bool> StopSetupAPCore()
    {
        try
        {
            Logger.LogToFile("[SetupAPManager] - StopSetupAP(): Stopping setup AP...");
            if (!await IsSetupAPRunning())
                return true;

            string result = await Shell.ExecuteAsync(
                $"nmcli connection down {SETUP_AP_INTERNAL_CONNECTION_NAME}");

            if (result.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - StopSetupAP(): Failed to stop setup AP. NativeResult: {result}", LogSeverity.Danger);
                return false;
            }

            if (await IsSetupAPRunning())
            {
                Logger.LogToFile("[SetupAPManager] - StopSetupAP(): Setup AP is still active after stopping.", LogSeverity.Danger);
                return false;
            }

            Logger.LogToFile("[SetupAPManager] - StopSetupAP(): Setup AP stopped.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - StopSetupAP(): Exception occurred.", ex);
            return false;
        }
    }

    private static async Task<bool> ActivateSetupAP()
    {
        if (!await IsWirelessReady() || !await EnsureAp0InterfaceExists())
            return false;

        string profileResult = await Shell.ExecuteAsync(
            $"LC_ALL=C nmcli -g connection.type,connection.interface-name,802-11-wireless.ssid,802-11-wireless.mode,802-11-wireless.band,ipv4.method,connection.autoconnect connection show {SETUP_AP_INTERNAL_CONNECTION_NAME}");

        string expectedProfile = $"802-11-wireless\n{SETUP_AP_INTERFACE_NAME}\n{SETUP_EXTERNAL_AP_SSID}\nap\nbg\nshared\nno";
        if (profileResult.Trim() != expectedProfile)
        {
            Logger.LogToFile("[SetupAPManager] - ActivateSetupAP(): Setup AP profile is missing or invalid. Recreating setup AP...");
            if (!await CreateSetupAPProfile())
                return false;
        }

        string result = await Shell.ExecuteAsync(
            $"nmcli --wait 30 connection up {SETUP_AP_INTERNAL_CONNECTION_NAME} ifname {SETUP_AP_INTERFACE_NAME}", 35);

        if (result.Contains("Error:"))
        {
            Logger.LogToFile($"[SetupAPManager] - StartSetupAP(): Failed to start setup AP. NativeResult: {result}", LogSeverity.Danger);
            return false;
        }

        if (!await IsSetupAPRunning())
        {
            Logger.LogToFile($"[SetupAPManager] - StartSetupAP(): Setup AP is not active after starting. NativeResult: {result}", LogSeverity.Danger);
            return false;
        }

        return true;
    }

    private static async Task<bool> IsWirelessReady()
    {
        if (!await DoesInterfaceExist(WLAN_INTERFACE_NAME))
        {
            Logger.LogToFile("[SetupAPManager] - IsWirelessReady(): wlan0 is not available.", LogSeverity.Warning);
            return false;
        }

        string radioResult = await Shell.ExecuteAsync("LC_ALL=C nmcli radio wifi");
        string rfkillResult = await Shell.ExecuteAsync("LC_ALL=C rfkill --noheadings --output SOFT,HARD list wifi");
        string linkResult = await Shell.ExecuteAsync($"ip -o link show {WLAN_INTERFACE_NAME}");

        bool wifiBlocked = string.IsNullOrWhiteSpace(rfkillResult) || rfkillResult.Contains("Error:");
        foreach (string state in rfkillResult.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (state != "unblocked")
            {
                wifiBlocked = true;
                break;
            }
        }

        bool wlanUp = false;
        string[] linkDetails = linkResult.Split('<', '>');
        if (!linkResult.Contains("Error:") && linkDetails.Length > 1)
        {
            foreach (string flag in linkDetails[1].Split(','))
            {
                if (flag == "UP")
                {
                    wlanUp = true;
                    break;
                }
            }
        }

        bool ready = radioResult.Trim() == "enabled" && !wifiBlocked && wlanUp;

        if (!ready)
        {
            Logger.LogToFile($"[SetupAPManager] - IsWirelessReady(): Wireless interface is not ready. NativeResult: {radioResult} {rfkillResult} {linkResult}", LogSeverity.Warning);
        }

        return ready;
    }

    private static async Task<bool> EnsureAp0InterfaceExists()
    {
        try
        {
            if (!await DoesInterfaceExist(SETUP_AP_INTERFACE_NAME))
            {
                string addResult = await Shell.ExecuteAsync(
                    $"sudo iw dev {WLAN_INTERFACE_NAME} interface add {SETUP_AP_INTERFACE_NAME} type __ap");

                if (addResult.Contains("Error:"))
                {
                    Logger.LogToFile($"[SetupAPManager] - EnsureAp0InterfaceExists(): Failed to create ap0. NativeResult: {addResult}", LogSeverity.Danger);
                    return false;
                }
            }

            string upResult = await Shell.ExecuteAsync($"sudo ip link set {SETUP_AP_INTERFACE_NAME} up");
            if (upResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - EnsureAp0InterfaceExists(): Failed to bring ap0 online. NativeResult: {upResult}", LogSeverity.Danger);
                return false;
            }

            if (!await DoesInterfaceExist(SETUP_AP_INTERFACE_NAME))
            {
                Logger.LogToFile("[SetupAPManager] - EnsureAp0InterfaceExists(): ap0 is not available after creation.", LogSeverity.Danger);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - EnsureAp0InterfaceExists(): Exception occurred.", ex);
            return false;
        }
    }

    private static async Task<bool> UnblockWlanModule()
    {
        try
        {
            // A failed activation with a ready radio can require resetting NetworkManager.
            bool restartNetworkManager = await IsWirelessReady();
            string rfkillResult = await Shell.ExecuteAsync("sudo rfkill unblock wifi");
            if (rfkillResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - UnblockWlanModule(): Failed to unblock wifi. NativeResult: {rfkillResult}", LogSeverity.Danger);
                return false;
            }

            string managerResult = await Shell.ExecuteAsync("LC_ALL=C nmcli -t -f RUNNING general");
            if (restartNetworkManager || managerResult.Trim() != "running")
            {
                string restartResult = await Shell.ExecuteAsync("sudo systemctl restart NetworkManager", 30);
                if (restartResult.Contains("Error:"))
                {
                    Logger.LogToFile($"[SetupAPManager] - UnblockWlanModule(): Failed to restart NetworkManager. NativeResult: {restartResult}", LogSeverity.Danger);
                    return false;
                }

                await Task.Delay(3000);
            }

            string radioResult = await Shell.ExecuteAsync("sudo nmcli radio wifi on");
            if (radioResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - UnblockWlanModule(): Failed to turn on wifi radio. NativeResult: {radioResult}", LogSeverity.Danger);
                return false;
            }

            string wlanUpResult = await Shell.ExecuteAsync($"sudo ip link set {WLAN_INTERFACE_NAME} up");
            if (wlanUpResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - UnblockWlanModule(): Failed to set wlan0 up. NativeResult: {wlanUpResult}", LogSeverity.Danger);
                return false;
            }

            if (!await IsWirelessReady())
                return false;

            Logger.LogToFile("[SetupAPManager] - UnblockWlanModule(): Successfully unblocked WLAN module.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogExceptionToFile("[SetupAPManager] - UnblockWlanModule(): Exception occurred.", ex);
            return false;
        }
    }

    private static async Task<bool> RecoverWireless()
    {
        if (!await UnblockWlanModule())
            return false;

        if (await DoesInterfaceExist(SETUP_AP_INTERFACE_NAME))
        {
            string deleteResult = await Shell.ExecuteAsync($"sudo iw dev {SETUP_AP_INTERFACE_NAME} del");
            if (deleteResult.Contains("Error:"))
            {
                Logger.LogToFile($"[SetupAPManager] - RecoverWireless(): Failed to delete ap0. NativeResult: {deleteResult}", LogSeverity.Danger);
                return false;
            }
        }

        if (!await EnsureAp0InterfaceExists())
            return false;

        return await CreateSetupAPProfile();
    }

    private static async Task<bool> DoesInterfaceExist(string interfaceName)
    {
        string result = await Shell.ExecuteAsync($"ip link show {interfaceName}");

        return !string.IsNullOrWhiteSpace(result) &&
               !result.Contains("does not exist") &&
               !result.Contains("Cannot find device") &&
               !result.Contains("Error:");
    }
}
