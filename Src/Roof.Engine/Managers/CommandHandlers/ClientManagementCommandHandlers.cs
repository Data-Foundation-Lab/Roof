using Roof.Common.Networking;
using Roof.Common.Tools;
using Roof.Engine.Structs;
using Roof.Engine.Tools;

namespace Roof.Engine.Managers.CommandHandlers
{
    internal class ClientManagementCommandHandlers
    {
        public static async Task RegisteredClientConnectionHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile($"[ClientManagementCommandHandlers]: Client [{client.ClientID}] requested connection.");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                var config = ConfigurationManager.GetConfiguration();
                client.WithStateLock(c =>
                {
                    c.FSManager = new ClientFileSystemManager(config.RegisteredClients[c.ClientID].OSSystemUsername);
                    c.IsVerified = true;
                    c.IsNounceVerified = true;
                    c.Nounce = "";
                });

                /*RoofClientConfig rfClientConfig = config.RegisteredClients[client.ClientID];
                if (rfClientConfig.AccessCount >= 5 || (rfClientConfig.AccessCount + 1) >= 5) //Require rotation after 5 sessions
                {
                    //Rotate pregenerated encryption fabs....
                    Logger.LogToFile($"[ClientManagementCommandHandlers]: Encryption fabs rotation is required for client [{client.ClientID}]");

                    //ROTATE....

                    //Reset to 0
                    config.RegisteredClients[client.ClientID].AccessCount = 0;
                }*/

                //Increase access count
                config.RegisteredClients[client.ClientID].AccessCount += 1;

                Logger.LogToFile($"[ClientManagementCommandHandlers]: Client [{client.ClientID}] connection successfully accepted.");
                
                respPacket.Status = true;
                respPacket.AddResponse("CONNECT_COMMAND_RESULT");

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ClientManagementCommandHandlers] - RegisteredClientConnectionHandler(): Exception occurred while trying to handle registered client connection request.", ex);
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("CONNECT_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task RotateRootPasswordHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers] - RotateRootPasswordHandler(): Root password rotation was requested. Rotating root password...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                string newRootPassword = GenerateAccountPassword();
                var escapedPassword = SafePath.ShellQuote("roof:" + newRootPassword);
                var chpasswdResult = await Shell.ExecuteAsync($"printf '%s\n' {escapedPassword} | sudo chpasswd");

                if(chpasswdResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateRootPasswordHandler(): Failed to rotate root password. NativeResult: {chpasswdResult}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("ROTATE_ROOTPASSWORD_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                Logger.LogToFile("[ClientManagementCommandHandlers] - RotateRootPasswordHandler(): Root password has been rotated successfully.");

                respPacket.Status = true;
                respPacket.AddResponse("ROTATE_ROOTPASSWORD_COMMAND_RESULT");
                respPacket.AddResponse(newRootPassword);
                
                await ClientManager.SendMessageToClient(client, respPacket);
                Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateRootPasswordHandler(): Sent rotated root password to {client.EndPoint}");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - RotateRootPasswordHandler(): Exception occured while trying to rotate root password.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("ROTATE_ROOTPASSWORD_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task RotateClientPasswordHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Client password rotation was requested. Rotating client password...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                string newClientPassword = GenerateAccountPassword();
                if (!SecurityManager.IsValidClientID(client.ClientID))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("ROTATE_CLIENTPASSWORD_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid client ID.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var escapedPassword = SafePath.ShellQuote(client.ClientID + ":" + newClientPassword);
                
                var chpasswdResult = await Shell.ExecuteAsync($"printf '%s\n' {escapedPassword} | sudo chpasswd");
                if(chpasswdResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Failed to rotate client password. NativeResult: {chpasswdResult}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("ROTATE_CLIENTPASSWORD_COMMAND_RESULT");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                Logger.LogToFile("[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Client password has been rotated successfully.");

                respPacket.Status = true;
                respPacket.AddResponse("ROTATE_CLIENTPASSWORD_COMMAND_RESULT");
                respPacket.AddResponse(newClientPassword);

                await ClientManager.SendMessageToClient(client, respPacket);
                Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Sent rotated client password to {client.EndPoint}");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - RotateClientPasswordHandler(): Exception occured while trying to rotate client password.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("ROTATE_CLIENTPASSWORD_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task RotateEncryptionHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers] - RotateEncryptionHandler(): Encryption rotation was requested. Rotating encryption...");
                
                (string generatedEncryptionIV, string generatedEncryptionKey) = Security.AES256.GenerateAESIVAndKey();
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = true;
                respPacket.AddResponse("ROTATE_ENCRYPTION_COMMAND_RESULT");
                respPacket.AddResponse(generatedEncryptionKey);
                respPacket.AddResponse(generatedEncryptionIV);
                
                // Keep the old key cached for the response, and only announce a persisted rotation.
                var oldEncryptionInfo = await client.GetEncryptionInfo();
                if (oldEncryptionInfo.Item1 == null || oldEncryptionInfo.Item2 == null)
                    throw new IOException("Failed to load the current client encryption key.");
                if (!await ConfigurationManager.SaveRoofClientNetworkEncryptionKey(client.ClientID, generatedEncryptionKey, generatedEncryptionIV))
                    throw new IOException("Failed to save the rotated client encryption key.");

                await ClientManager.SendMessageToClient(client, respPacket);
                Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateEncryptionHandler(): Encryption has been rotated successfully. Sent to {client.EndPoint}");
                    
                client.SetEncryptionInfo(generatedEncryptionKey, generatedEncryptionIV);
                    
                Logger.LogToFile($"[ClientManagementCommandHandlers] - RotateEncryptionHandler(): Saved the rotated encryption.");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - RotateEncryptionHandler(): Exception occured while trying to rotate encryption.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("ROTATE_ENCRYPTION_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task CreateUserHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers] - CreateUserHandler(): User creation was requested. Creating user...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                string username = args[0];
                string password = args[1];

                if (!SecurityManager.IsValidClientID(username))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - CreateUserHandler(): Username is in incorrect format. Rejecting request...", LogSeverity.Warning);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("Incorrect username format.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string? passwordError = SecurityManager.GetPasswordValidationError(password);
                if (passwordError != null)
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): {passwordError} Rejecting request...", LogSeverity.Warning);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse(passwordError);

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var escapedUsername = username;
                var escapedPassword = SafePath.ShellQuote(username + ":" + password);
                SafePath.CanonicalizeUnder($"/home/{escapedUsername}/playground", $"/home/{escapedUsername}");

                Logger.LogToFile($"[ClientManagementCommandHandlers]: User creation was requested. Creating a new roof user account for {escapedUsername}...");
                
                string userlistResult = await Shell.ExecuteAsync($"getent passwd -- {SafePath.ShellQuote(escapedUsername)}; status=$?; " +
                    "case $status in 0|2) ;; *) exit \"$status\" ;; esac");
                if (userlistResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Failed to look up Linux account: " + userlistResult);

                bool userExists = userlistResult.StartsWith(escapedUsername + ":", StringComparison.Ordinal);
                var conf = ConfigurationManager.GetConfiguration();
                // Resume only accounts already recorded for this client by a previous attempt.
                if (userExists && conf.RegisteredClients[client.ClientID].OSSystemUsername != escapedUsername)
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Failed to create a roof user account for the requested user {escapedUsername}. User already exists.", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("User already exists.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                string createAccountCommand = userExists ? "" :
                    $"sudo adduser --gecos \"{escapedUsername},,,\" --disabled-password --force-badname {escapedUsername} && ";
                string addUserResult = await Shell.ExecuteAsync(createAccountCommand + $"printf '%s\n' {escapedPassword} | sudo chpasswd");
                if (addUserResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Failed to create a roof user account for the requested user {escapedUsername}. NativeResult: {addUserResult}", LogSeverity.Danger);
                    
                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                Logger.LogToFile($"[ClientManagementCommandHandlers]: Roof user account has been created successfully for {escapedUsername}.");

                //Attach username to client id.
                conf.RegisteredClients[client.ClientID].OSSystemUsername = escapedUsername;
                await ConfigurationManager.SaveConfiguration(conf);
                
                Logger.LogToFile($"[ClientManagementCommandHandlers]: Configuring SFTP jail for {escapedUsername}...");
                
                string addUserToGroupResult = await Shell.ExecuteAsync($"sudo usermod -aG roofsftpusers {escapedUsername}");
                if (!string.IsNullOrEmpty(addUserToGroupResult))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Failed to add a user to the group: roofsftpusers. NativeResult: {addUserToGroupResult}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                Logger.LogToFile($"[ClientManagementCommandHandlers]: Added user {escapedUsername} to the group: roofsftpusers");

                string chownRootUserDirResult = await Shell.ExecuteAsync($"sudo chown root:root /home/{escapedUsername}");
                string chmod755UserDirResult = await Shell.ExecuteAsync($"sudo chmod 755 /home/{escapedUsername}");

                if (!string.IsNullOrEmpty(chownRootUserDirResult) || !string.IsNullOrEmpty(chmod755UserDirResult))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Failed to set root writable for /home/{escapedUsername}." +
                                     $"\nchownRootUserDirResult: {chownRootUserDirResult}" +
                                     $"\nchmod755UserDirResult: {chmod755UserDirResult}");

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                Logger.LogToFile($"[ClientManagementCommandHandlers]: Set /home/{escapedUsername} to be only writable by root only.");

                string restartSSH = await Shell.ExecuteAsync("sudo systemctl restart ssh");
                if (!string.IsNullOrEmpty(restartSSH))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Failed to restart ssh service. NativeResult: {restartSSH}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                Logger.LogToFile($"[ClientManagementCommandHandlers]: Successfully configured SFTP jail for {escapedUsername}");

                client.WithStateLock(c => c.FSManager = new ClientFileSystemManager(escapedUsername));

                respPacket.Status = true;
                respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - CreateUserHandler(): Exception occurred while trying to create a roof user account.", ex);
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("CREATE_ROOF_USER_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task GetRoofIDHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers]: RoofID was requested. Sending RoofID...");
                
                var conf = ConfigurationManager.GetConfiguration();

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = true;
                respPacket.AddResponse("GET_ROOFID_COMMAND_RESULT");
                respPacket.AddResponse(conf.RoofIdentifier);
                
                await ClientManager.SendMessageToClient(client, respPacket);
                Logger.LogToFile($"[ClientManagementCommandHandlers]: RoofID has been sent to {client.EndPoint}");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - GetRoofIDHandler(): Exception occured while trying to get Roof ID.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_ROOFID_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task GetWifiListHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi list was requested. Processing wifi list....");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi list was requested while operating in remote mode. Rejecting request...", LogSeverity.Warning);
                    
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_WIFI_LIST_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid operation path.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string wifiRescanResult = await Shell.ExecuteAsync("sudo nmcli device wifi rescan");
                if (!string.IsNullOrEmpty(wifiRescanResult))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - GetWifiListHandler(): Wifi rescan failed.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_WIFI_LIST_COMMAND_RESULT");
                    respPacket.AddResponse("Wifi rescanning failed.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string wifiListNmcliResult = await Shell.ExecuteAsync("nmcli -t -f IN-USE,BSSID,SSID,CHAN,SIGNAL,SECURITY device wifi list");

                var wifiList = WifiAPLister.ListBestPerSsid(wifiListNmcliResult);
                
                Logger.LogToFile("[ClientManagementCommandHandlers] - GetWifiListHandler(): Wifi scan completed.");

                respPacket.Status = true;
                respPacket.AddResponse("GET_WIFI_LIST_COMMAND_RESULT");
                foreach (var ap in wifiList)
                    respPacket.AddResponse(ap.Ssid);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - GetWifiListHandler(): Exception occured while trying to get list of Wi-Fis.", ex);
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_WIFI_LIST_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task ConnectToWifiHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi connection was requested. Attempting to connect...");

                RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

                if (args == null || args.Count < 2)
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid wifi connection request.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string ssid = args[0];
                string password = args[1];
                if (!SecurityManager.IsValidText(ssid, 32) || !SecurityManager.IsValidText(password, 64) ||
                    System.Text.Encoding.UTF8.GetByteCount(password) < 8 ||
                    (System.Text.Encoding.UTF8.GetByteCount(password) == 64 && !password.All(Uri.IsHexDigit)))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid SSID or Wi-Fi password.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                // Use a Roof-owned connection profile name instead of using the SSID directly.
                // This avoids conflicts with stale user/system profiles that may share the SSID name.
                string connectionName = $"Roof-WiFi-{ssid}";

                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi connection was requested while operating in remote mode. Rejecting request....", LogSeverity.Warning);

                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid operation path.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                await Shell.ExecuteAsync("sudo nmcli radio wifi on");
                await Shell.ExecuteAsync("sudo nmcli device set wlan0 managed yes");

                // Give NetworkManager a moment to settle, especially after Setup AP/ap0 teardown.
                await Task.Delay(1500);

                string wifiRescanResult = await Shell.ExecuteAsync("sudo nmcli device wifi rescan");
                if (wifiRescanResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Wifi rescanning failed.");

                    Logger.LogToFile(
                        "[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Wifi rescan failed." +
                        $"\nNativeResult: {wifiRescanResult}",
                        LogSeverity.Danger
                    );

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string wifiListNmcliResult = await Shell.ExecuteAsync(
                    "nmcli -t -f IN-USE,BSSID,SSID,CHAN,SIGNAL,SECURITY device wifi list"
                );

                var wifiList = WifiAPLister.ListBestPerSsid(wifiListNmcliResult);
                if (!wifiList.Any(ap => ap.Ssid == ssid))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Requested wifi was not found.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid wifi.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                // Remove stale or broken Roof-created profile first.
                await Shell.ExecuteAsync($"sudo nmcli connection delete {ShellQuote(connectionName)} 2>/dev/null");

                string createWifiProfileCommand =
                    "sudo nmcli connection add " +
                    "type wifi " +
                    "ifname wlan0 " +
                    $"con-name {ShellQuote(connectionName)} " +
                    $"ssid {ShellQuote(ssid)} " +
                    "wifi-sec.key-mgmt wpa-psk " +
                    $"wifi-sec.psk {ShellQuote(password)}";

                string createWifiProfileResult = await Shell.ExecuteAsync(createWifiProfileCommand);

                if (createWifiProfileResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Wifi profile creation failed.");

                    Logger.LogToFile(
                        "[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Failed to create wifi profile." +
                        $"\nNativeResult: {createWifiProfileResult}",
                        LogSeverity.Danger
                    );

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string wifiConnectionResult = await Shell.ExecuteAsync(
                    $"sudo nmcli connection up {ShellQuote(connectionName)} ifname wlan0"
                );

                if (wifiConnectionResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Wifi connection failed.");

                    Logger.LogToFile(
                        "[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Failed to connect to wifi." +
                        $"\nNativeResult: {wifiConnectionResult}",
                        LogSeverity.Danger
                    );

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                Logger.LogToFile("[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Wifi connected successfully.");

                respPacket.Status = true;
                respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                respPacket.AddResponse("Wifi connected successfully.");

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile(
                    "[ClientManagementCommandHandlers] - ConnectToWifiHandler(): Exception occured while trying to connect to a wifi.",
                    ex
                );

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("CONNECT_TO_WIFI_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        private static string GenerateAccountPassword()
        {
            string password;
            do
            {
                password = Security.GenerateStrongPassword(16);
            }
            while (!SecurityManager.IsValidPassword(password));
            return password;
        }

        private static string ShellQuote(string value)
        {
            return SafePath.ShellQuote(value);
        }
        
        public static async Task GetCurrentWifiHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers]: Current wifi connection was requested. Processing...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Current wifi was requested while operating in remote mode. Rejecting request....", LogSeverity.Warning);
                    
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_CURRENT_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid operation path.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                string currentWifiNmcliResult = await Shell.ExecuteAsync("nmcli -t -f active,ssid dev wifi | grep '^yes' | cut -d: -f2");
                if (string.IsNullOrEmpty(currentWifiNmcliResult) || currentWifiNmcliResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_CURRENT_WIFI_COMMAND_RESULT");
                    
                    if (currentWifiNmcliResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogToFile($"[ClientManagementCommandHandlers] - GetCurrentWifiHandler(): Failed to get current wifi." +
                                         $"\nNativeResult: {currentWifiNmcliResult}");
                        
                        respPacket.AddResponse("Internal engine error.");
                        
                        await ClientManager.SendMessageToClient(client, respPacket);
                        return;
                    }
                    
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi command could not complete because wifi is disconnected.", LogSeverity.Warning);
                    respPacket.AddResponse("Not connected to a wifi.");
                    await ClientManager.SendMessageToClient(client, respPacket);
                    
                    return;
                }
                
                Logger.LogToFile("[ClientManagementCommandHandlers] - GetCurrentWifiHandler(): Current wifi query completed.");

                respPacket.Status = true;
                respPacket.AddResponse("GET_CURRENT_WIFI_COMMAND_RESULT");
                respPacket.AddResponse(currentWifiNmcliResult);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - GetCurrentWifiHandler(): Exception occured while trying to get current wifi.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_CURRENT_WIFI_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task ForgetCurrentWifiHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers]: Forget current wifi was requested. Processing...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Forget current wifi was requested while operating in remote mode. Rejecting request....", LogSeverity.Warning);
                    
                    respPacket.Status = false;
                    respPacket.AddResponse("FORGET_CURRENT_WIFI_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid operation path.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                string currentWifiNConnectionProfileNmcliResult = await Shell.ExecuteAsync("nmcli -g GENERAL.CONNECTION device show wlan0");
                if (string.IsNullOrEmpty(currentWifiNConnectionProfileNmcliResult) || currentWifiNConnectionProfileNmcliResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("FORGET_CURRENT_WIFI_COMMAND_RESULT");
                    
                    if (currentWifiNConnectionProfileNmcliResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogToFile($"[ClientManagementCommandHandlers] - ForgetCurrentWifiHandler(): Failed to get current wifi to forget." +
                                         $"\nNativeResult: {currentWifiNConnectionProfileNmcliResult}");
                        
                        respPacket.AddResponse("Internal engine error.");
                        
                        await ClientManager.SendMessageToClient(client, respPacket);
                        return;
                    }
                    
                    Logger.LogToFile("[ClientManagementCommandHandlers]: Wifi command could not complete because wifi is disconnected.", LogSeverity.Warning);
                    respPacket.AddResponse("Not connected to a wifi.");
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                string nmcliDelConnResult = await Shell.ExecuteAsync($"sudo nmcli connection delete {ShellQuote(currentWifiNConnectionProfileNmcliResult)}");
                if (string.IsNullOrEmpty(nmcliDelConnResult) || nmcliDelConnResult.Contains("Error:", StringComparison.OrdinalIgnoreCase) || !nmcliDelConnResult.Contains("successfully deleted."))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("FORGET_CURRENT_WIFI_COMMAND_RESULT");
                    
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - ForgetCurrentWifiHandler(): Failed to forget current wifi." +
                                     $"\nNativeResult: {nmcliDelConnResult}");
                        
                    respPacket.AddResponse("Internal engine error.");
                        
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                
                Logger.LogToFile("[ClientManagementCommandHandlers] - ForgetCurrentWifiHandler(): Current wifi forgotten successfully.");

                respPacket.Status = true;
                respPacket.AddResponse("FORGET_CURRENT_WIFI_COMMAND_RESULT");
                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - ForgetCurrentWifiHandler(): Exception occured while trying to forget current wifi.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("FORGET_CURRENT_WIFI_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task RestartRoofHandler(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("[ClientManagementCommandHandlers] - RestartRoofHandler(): Roof restart was requested. Restarting...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                var syncResult = await Shell.ExecuteAsync("sudo sync");
                if(syncResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogToFile($"[ClientManagementCommandHandlers] - RestartRoofHandler(): Failed to sync current OS state. NativeResult: {syncResult}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("RESTART_ROOF_COMMAND_RESULT");
                    respPacket.AddResponse("Internal engine error.");
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                //Delay it so that the OS state sync is actually stablised.
                await Task.Delay(5000);
                
                Logger.LogToFile("[ClientManagementCommandHandlers] - RestartRoofHandler(): Successfully synchronized current OS state. Performing restart...");

                respPacket.Status = true;
                respPacket.AddResponse("RESTART_ROOF_COMMAND_RESULT");
                await ClientManager.SendMessageToClient(client, respPacket);

                ShutdownManager.RequestReboot();
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagementCommandHandlers] - RestartRoofHandler(): Exception occured while trying to restart Roof.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("RESTART_ROOF_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
    }
}
