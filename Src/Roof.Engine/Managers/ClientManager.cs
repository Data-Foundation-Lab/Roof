using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Roof.Common.Networking;
using Roof.Engine.Tools;
using Roof.Engine.Events;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.IO;
using System.Net.Http;
using Roof.Common.Tools;
using Roof.Common.Networking.RoofData;
using Roof.Common.Networking.RNMS;
using Roof.Common.Networking.RNMS.WebSocket;
using Roof.Engine.Services;
using Roof.Engine.Structs;
using Roof.Engine.Managers.CommandHandlers;

namespace Roof.Engine.Managers
{
    public class ClientManager
    {
        public const int LOCAL_PRESENCE_BEAM_UDP_PORT = 6969; //Should really change this port number... lol
        public const int DEFAULT_LOCAL_CLIENT_TCP_PORT = 8671; //Should be used with-in the local network context.
        
        //Command structure = "COMMAND PARAMETER PARAMETER2 ..."
        private static Dictionary<List<string>, Delegate> _clientCommandDispatchTable = new Dictionary<List<string>, Delegate>
        {
            //TODO: LIMIT COMMANDS FOR FRESH AND SETUP RESUMING CLIENTS
            
            //CLIENT-RELATED COMMANDS
            {new List<string>() { "CONNECT" }, ClientManagementCommandHandlers.RegisteredClientConnectionHandler },
            {new List<string>() { "ROTATE_ROOTPASSWORD" }, ClientManagementCommandHandlers.RotateRootPasswordHandler},
            {new List<string>() { "ROTATE_CLIENTPASSWORD" }, ClientManagementCommandHandlers.RotateClientPasswordHandler},
            {new List<string>() { "ROTATE_ENCRYPTION" }, ClientManagementCommandHandlers.RotateEncryptionHandler},
            {new List<string>() { "CREATE_ROOF_USER", "username", "password" }, ClientManagementCommandHandlers.CreateUserHandler},
            {new List<string>() { "GET_ROOFID" }, ClientManagementCommandHandlers.GetRoofIDHandler },
            {new List<string>() { "GET_WIFI_LIST" }, ClientManagementCommandHandlers.GetWifiListHandler },
            {new List<string>() { "CONNECT_TO_WIFI", "ssid", "password" }, ClientManagementCommandHandlers.ConnectToWifiHandler },
            {new List<string>() { "GET_CURRENT_WIFI", }, ClientManagementCommandHandlers.GetCurrentWifiHandler },
            {new List<string>() { "FORGET_CURRENT_WIFI", }, ClientManagementCommandHandlers.ForgetCurrentWifiHandler },
            {new List<string>() { "RESTART_ROOF", }, ClientManagementCommandHandlers.RestartRoofHandler },
            
            //FILESYSTEM-RELATED COMMANDS
            {new List<string>() { "GET_BASE_DIRECTORY" }, FileSystemCommandHandlers.FileSystemHandler_GetBaseDirectory},
            {new List<string>() { "GET_DIRECTORY", "hashedPath"}, FileSystemCommandHandlers.FileSystemHandler_GetDirectory},
            {new List<string>() { "GET_DIRECTORIES", "hashedPath", "serialised_json"}, FileSystemCommandHandlers.FileSystemHandler_GetDirectories},
            {new List<string>() { "CREATE_DIRECTORY", "hashedPath","serialised_json" }, FileSystemCommandHandlers.FileSystemHandler_CreateDirectory},
            {new List<string>() { "UPDATE_DIRECTORY", "hashedPath", "serialised_json" }, FileSystemCommandHandlers.FileSystemHandler_UpdateDirectory},
            {new List<string>() { "COPY_DIRECTORY", "copyingHashedPath", "targetHashedPath" }, FileSystemCommandHandlers.FileSystemHandler_CopyDirectory},
            {new List<string>() { "DELETE_DIRECTORY", "hashedPath" }, FileSystemCommandHandlers.FileSystemHandler_DeleteDirectory},
            {new List<string>() { "GET_FILE", "hashedPath" }, FileSystemCommandHandlers.FileSystemHandler_GetFile },
            {new List<string>() { "CREATE_FILE", "hashedPath", "serialised_json" }, FileSystemCommandHandlers.FileSystemHandler_CreateFile },
            {new List<string>() { "UPDATE_FILE", "hashedPath", "serialised_json" }, FileSystemCommandHandlers.FileSystemHandler_UpdateFile },
            {new List<string>() { "DELETE_FILE", "hashedPath" }, FileSystemCommandHandlers.FileSystemHandler_DeleteFile },
            {new List<string>() { "GET_FILE_DATA", "hashedPath" }, FileSystemCommandHandlers.FileSystemHandler_GetFileData },
        };

        private static readonly SemaphoreSlim _registrationGate = new(1, 1);

        private static NetworkMessageProvider _networkMessageProvider = new NetworkMessageProvider();
        private static NetworkMessageProvider? _remoteNetworkMessageProvider;

        private static readonly ConcurrentDictionary<string, RoofClient> _connectedClients = new();
        private static readonly ConcurrentDictionary<string, RoofClient> _firstTimeUnverifiedClients = new();

        public static void SetNetworkMessageProvider(NetworkMessageProvider networkMessageProvider)
        {
            _networkMessageProvider = networkMessageProvider;
        }

        public static void SetRemoteNetworkMessageProvider(NetworkMessageProvider? remoteNetworkMessageProvider)
        {
            _remoteNetworkMessageProvider = remoteNetworkMessageProvider;
        }

        public static RoofClient[] GetConnectedClientsSnapshot()
        {
            return _connectedClients.Values.ToArray();
        }

        public static bool TryGetConnectedClientByEndpoint(IPEndPoint? endPoint, out RoofClient client)
        {
            if (endPoint == null)
            {
                client = null;
                return false;
            }

            return _connectedClients.TryGetValue(GetEndpointKey(endPoint), out client);
        }

        public static bool TryAddConnectedClient(RoofClient client)
        {
            var key = GetClientKey(client);
            return key != null && _connectedClients.TryAdd(key, client);
        }

        public static RoofClient GetOrAddConnectedClient(IPEndPoint endPoint)
        {
            return _connectedClients.GetOrAdd(GetEndpointKey(endPoint), _ => new RoofClient { EndPoint = endPoint });
        }

        public static RoofClient GetOrAddRemoteClient(string clientId, string? fingerprint)
        {
            var key = GetRemoteClientKey(clientId, fingerprint);
            return _connectedClients.GetOrAdd(key, _ => new RoofClient
            {
                ClientID = clientId,
                IsInRemoteMode = true,
                GlobalRoutedFingerprint = fingerprint ?? string.Empty,
                LastPFHeartbeatReceivedTime = DateTime.UtcNow,
            });
        }

        public static bool RemoveConnectedClient(RoofClient client)
        {
            var key = GetClientKey(client);
            bool removed = key != null && ((ICollection<KeyValuePair<string, RoofClient>>)_connectedClients)
                .Remove(new KeyValuePair<string, RoofClient>(key, client));
            if (removed && client.WithStateLock(c => c.IsInRemoteMode))
                Logger.LogToFile("[ClientManager]: Remote client disconnected.");
            if (removed)
            {
                RemoveFirstTimeUnverifiedClient(client);
                client.Dispose();
            }
            return removed;
        }

        public static bool HasConnectedClient(IPEndPoint? endPoint)
        {
            return endPoint != null && _connectedClients.ContainsKey(GetEndpointKey(endPoint));
        }

        public static bool TryGetFirstTimeUnverifiedClientByEndpoint(IPEndPoint? endPoint, bool requireTcpInfoSent, out RoofClient client)
        {
            if (endPoint == null)
            {
                client = null;
                return false;
            }

            if (!_firstTimeUnverifiedClients.TryGetValue(GetEndpointKey(endPoint), out client))
                return false;

            return !requireTcpInfoSent || client.WithStateLock(c => c.IsFirstTimeTCPEncryptionInfoSent);
        }

        public static bool TryAddFirstTimeUnverifiedClient(RoofClient client)
        {
            if (client.EndPoint == null)
                return false;

            return _firstTimeUnverifiedClients.TryAdd(GetEndpointKey(client.EndPoint), client);
        }

        public static bool RemoveFirstTimeUnverifiedClient(RoofClient client)
        {
            if (client.EndPoint == null)
                return false;

            return ((ICollection<KeyValuePair<string, RoofClient>>)_firstTimeUnverifiedClients)
                .Remove(new KeyValuePair<string, RoofClient>(GetEndpointKey(client.EndPoint), client));
        }

        public static void RemoveStaleClients()
        {
            foreach (var client in GetConnectedClientsSnapshot())
                if (client.WithStateLock(c => !c.IsVerified && DateTime.UtcNow - c.ConnectedTime > TimeSpan.FromMinutes(2)))
                    RemoveConnectedClient(client);
        }

        private static string? GetClientKey(RoofClient client)
        {
            if (client.EndPoint != null)
                return GetEndpointKey(client.EndPoint);

            if (!string.IsNullOrEmpty(client.ClientID))
                return GetRemoteClientKey(client.ClientID, client.GlobalRoutedFingerprint);

            return null;
        }

        private static string GetEndpointKey(IPEndPoint endPoint)
        {
            return $"{endPoint.Address}:{endPoint.Port}";
        }

        private static string GetRemoteClientKey(string clientId, string? fingerprint)
        {
            return $"remote:{clientId}:{fingerprint ?? string.Empty}";
        }
        
        public static async Task HandleClientCommandPacket(RoofClient client, byte[] packetBuffer, int packetBufferLength)
        {
            await client.CommandGate.WaitAsync();
            try
            {
                await HandleClientCommandPacketCore(client, packetBuffer, packetBufferLength);
            }
            finally
            {
                client.CommandGate.Release();
            }
        }

        private static async Task HandleClientCommandPacketCore(RoofClient client, byte[] packetBuffer, int packetBufferLength)
        {
            var config = ConfigurationManager.GetConfiguration();

            if (client.WithStateLock(c => c.IsDisposed))
                return;
            var expectedClientId = client.WithStateLock(c => string.IsNullOrEmpty(c.ClientID) ? null : c.ClientID);
            string? authenticatedClientId = null;
            string? decryptedPacket = null;
            try
            {
                //We're trying to perform hit test on decryption as the incoming packet is encrypted.
                foreach (var rfClient in config.RegisteredClients)
                {
                    if (expectedClientId != null && !string.Equals(expectedClientId, rfClient.Key, StringComparison.Ordinal))
                        continue;
                    if (File.Exists(ConfigurationManager.ClientKeyPath(rfClient.Key)))
                    {
                        try
                        {
                            var readClientNEKContent = await File.ReadAllBytesAsync(ConfigurationManager.ClientKeyPath(rfClient.Key));
                
                            if (readClientNEKContent.Length != 48)
                                continue;

                            byte[] readClientNEK = readClientNEKContent.Take(32).ToArray();  // AES-256 = 32 bytes
                            byte[] readClientNIV = readClientNEKContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                            string loadedClientNEK = Convert.ToBase64String(readClientNEK);
                            string loadedClientNIV = Convert.ToBase64String(readClientNIV);
                            
                            string possibleDecryptedPacket = Security.AES256.ManagedNetworkDecrypt(
                                loadedClientNEK,
                                loadedClientNIV,
                                    packetBuffer,
                                    packetBufferLength);

                            if (possibleDecryptedPacket.IsStringJson())
                            {
                                authenticatedClientId = rfClient.Key;
                                decryptedPacket = possibleDecryptedPacket;
                                break;
                            }
                        }
                        catch
                        {
                            // Wrong client key/IV for this packet. Try the next registered client.
                            continue;
                        }
                    }
                }

                if (decryptedPacket == null)
                {
                    Logger.LogToFile($"[ClientManager] - HandleClientCommandPacket(): Failed to decrypt client command packet.", LogSeverity.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManager] - HandleClientCommandPacket(): Exception occurred while trying to decrypt client command packet.", ex);
                return;
            }
            
            RoofNetworkNativeRequestPacket? packet;
            try
            {
                packet = JsonSerializer.Deserialize(decryptedPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ClientManager] - HandleClientCommandPacket(): Failed to deserialize decrypted command packet.", ex);
                return;
            }

            if(packet == null || !SecurityManager.IsValidClientID(packet.ClientID) ||
                !string.Equals(packet.ClientID, authenticatedClientId, StringComparison.Ordinal) ||
                !config.RegisteredClients.ContainsKey(packet.ClientID) ||
                (expectedClientId != null && !string.Equals(packet.ClientID, expectedClientId, StringComparison.Ordinal)))
            {
                Logger.LogToFile("[ClientManager] - HandleClientCommandPacket(): The command packet was invalid.", LogSeverity.Warning);
                return;
            }

            if (packet.Requests == null || string.IsNullOrEmpty(packet.Requests.FirstOrDefault()))
            {
                Logger.LogToFile("[ClientManager] - HandleClientCommandPacket(): The command prefix was invalid.", LogSeverity.Warning);
                return;
            }

            var commandString = packet.Requests.FirstOrDefault();
            if (packet.Requests.Any(value => value == null) || !_clientCommandDispatchTable.Keys.Any(command =>
                string.Equals(command[0], commandString, StringComparison.OrdinalIgnoreCase) && command.Count == packet.Requests.Count))
            {
                var command = _clientCommandDispatchTable.Keys.FirstOrDefault(command =>
                    string.Equals(command[0], commandString, StringComparison.OrdinalIgnoreCase));
                if (command != null && command.Count != packet.Requests.Count)
                {
                    var prefix = command[0];
                    int expectedParams = command.Count - 1;
                    Logger.LogToFile($"[ClientManager] - HandleClientCommandPacket(): Command [{prefix}] expects {expectedParams} params but got {packet.Requests.Count - 1}.", LogSeverity.Warning);
                }
                Logger.LogToFile(
                    $"[ClientManager] - HandleClientCommandPacket(): The command prefix was not found or unsupported for [{commandString}].", LogSeverity.Warning);
                return;
            }
            if (client.WithStateLock(c => c.IsDisposed))
                return;
            client.WithStateLock(c => c.IsVerified = true);
            foreach (var previousClient in GetConnectedClientsSnapshot())
                if (!ReferenceEquals(previousClient, client) && previousClient.WithStateLock(c =>
                    c.ClientID == packet.ClientID && c.IsInRemoteMode == client.IsInRemoteMode))
                    RemoveConnectedClient(previousClient);

            if (client.WithStateLock(c => c.FSManager == null))
            {
                try
                {
                    client.WithStateLock(c =>
                    {
                        var username = config.RegisteredClients[packet.ClientID].OSSystemUsername;
                        if (!string.IsNullOrEmpty(username))
                            c.FSManager = new ClientFileSystemManager(username);

                        c.ClientID = packet.ClientID;
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile("[ClientManager] - HandleClientCommandPacket(): Failed to initialise client filesystem.", ex);

                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse($"{commandString.ToUpperInvariant()}_COMMAND_RESULT");
                    respPacket.AddResponse("Failed to initialise client filesystem.");

                    // Reply using the request identity without marking the client initialised.
                    var responseClient = client.WithStateLock(c => new RoofClient
                    {
                        ClientID = packet.ClientID,
                        EndPoint = c.EndPoint,
                        IsInRemoteMode = c.IsInRemoteMode
                    });
                    await SendMessageToClient(responseClient, respPacket);
                    RemoveConnectedClient(client);
                    return;
                }
            }

            bool commandFound = false;

            foreach (var cmd in _clientCommandDispatchTable)
            {
                var prefix = cmd.Key[0];
                if (!string.Equals(prefix, commandString, StringComparison.OrdinalIgnoreCase))
                    continue;

                int expectedParams = cmd.Key.Count - 1;

                if (packet.Requests.Count - 1 != expectedParams)
                {
                    Logger.LogToFile($"[ClientManager] - HandleClientCommandPacket(): Command [{prefix}] expects {expectedParams} params but got {packet.Requests.Count - 1}.", LogSeverity.Warning);
                    break;
                }

                var args = packet.Requests.Skip(1).ToList();  // last arg may include spaces

#if DEBUG
                Logger.Log($"[ClientManager] - HandleClientCommandPacket(): Executing [{prefix}]");
#else
                Logger.LogToFile($"[ClientManager]: Executing [{prefix}]");
#endif

                try
                {
                    // Complete this command before handling the next packet from this connection.
                    if (cmd.Value.DynamicInvoke(client, args) is Task handlerTask)
                        await handlerTask;
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile($"[ClientManager] - HandleClientCommandPacket(): Exception occurred while trying to execute command [{prefix}].", ex);
                }

                commandFound = true;
                break;
            }

            if (!commandFound)
            {
                Logger.LogToFile(
                    $"[ClientManager] - HandleClientCommandPacket(): The command prefix was not found or unsupported for [{commandString}].", LogSeverity.Warning);
            }
        }

        public static async Task HandleFirstTimeClientTCPInfoRequest(NetworkMessageProvider networkMessageProvider, RoofClient client, string clientID, RoofConfiguration config)
        {
            await _registrationGate.WaitAsync();
            try
            {
                if (!SecurityManager.IsValidClientID(clientID) || config.RegisteredClients.ContainsKey(clientID) ||
                    client.WithStateLock(c => c.IsDisposed || c.IsVerified || c.IsFirstTimeTCPEncryptionInfoSent))
                    return;
                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile($"[ClientManager] - HandleFirstTimeClientTCPInfoRequest(): Received a TCP encryption info request from an unverified first-time client [{clientID}] while operating in remote mode. Rejecting request...", LogSeverity.Warning);
                    
                    var responsePacket = new RoofNetworkNativeResponsePacket();
                    responsePacket.Status = false;
                    responsePacket.AddResponse("TCP_ENCRYPTION_INFO");
                    responsePacket.AddResponse("Invalid operation path.");

                    string serialisedFailPacket = JsonSerializer.Serialize(responsePacket, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
                    byte[] failPacketBuffer = Encoding.UTF8.GetBytes(serialisedFailPacket);

                    await networkMessageProvider.SendMessage(failPacketBuffer, client.EndPoint);
                    return;
                }
                
                Logger.LogToFile($"[ClientManager] - HandleFirstTimeClientTCPInfoRequest(): Received a first-time client connection TCP encryption info request from an unverified client [{clientID}].");
                Logger.LogToFile("[ClientManager] - HandleFirstTimeClientTCPInfoRequest(): Generating encryption key and IV for the client.");
                
                (string pregeneratedEncryptionIV, string pregeneratedEncryptionKey) = Security.AES256.GenerateAESIVAndKey();

                var jsonKeyAndIVPacket = new RoofNetworkNativeResponsePacket();
                jsonKeyAndIVPacket.Status = true;
                jsonKeyAndIVPacket.AddResponse("TCP_ENCRYPTION_INFO");
                jsonKeyAndIVPacket.AddResponse(clientID);
                jsonKeyAndIVPacket.AddResponse(pregeneratedEncryptionKey);
                jsonKeyAndIVPacket.AddResponse(pregeneratedEncryptionIV);

                string serialisedPacket = JsonSerializer.Serialize(jsonKeyAndIVPacket, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
                byte[] packetBuffer = Encoding.UTF8.GetBytes(serialisedPacket);

                client.WithStateLock(c =>
                {
                    c.RegistrationClientID = clientID;
                    c.IsAdditionalRegistration = config.HasCompletedInitialSetup;
                    c.IsFirstTimeTCPEncryptionInfoSent = true;
                    c.FirstTimeTCPEncryptionKey = pregeneratedEncryptionKey;
                    c.FirstTimeTCPEncryptionIV = pregeneratedEncryptionIV;
                });
                if (!TryAddFirstTimeUnverifiedClient(client))
                {
                    RemoveConnectedClient(client);
                    return;
                }
                Logger.LogToFile("[ClientManager]: Added to first time unverified clients list.");

                //Send the pregenerated client TCP AES key and IV
                if (!await networkMessageProvider.SendMessage(packetBuffer, client.EndPoint))
                {
                    RemoveConnectedClient(client);
                    return;
                }
                Logger.LogToFile("[ClientManager]: Sent pre-generated AES key and IV to the client.");
                Logger.LogToFile("[ClientManager]: Waiting for the client to be verified through the secure tunnel...");
            }
            catch (Exception ex)
            {
                RemoveConnectedClient(client);
                Logger.LogExceptionToFile($"[ClientManager] - HandleFirstTimeClientTCPInfoRequest(): Exception occurred while trying to handle first-time client TCP info request.", ex);
            }
            finally
            {
                _registrationGate.Release();
            }
        }

        public static async Task HandleFirstTimeClientTCPEncryptionVerification(NetworkMessageProvider networkMessageProvider, RoofClient client, string packet)
        {
            await _registrationGate.WaitAsync();
            try
            {
                if (client.WithStateLock(c => c.IsInRemoteMode))
                {
                    Logger.LogToFile($"[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Received a client registration request from an unverified first-time client while operating in remote mode. Rejecting request...", LogSeverity.Warning);
                    
                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse("REGISTER_REQUEST");
                    respPacket.AddResponse("Invalid operation path.");
                    
                    await SendMessageToClient(client, respPacket);
                    return;
                }
            
                if (client.WithStateLock(c => c.IsDisposed || !c.IsFirstTimeTCPEncryptionInfoSent))
                    return;
                byte[] packetBuffer = Encoding.UTF8.GetBytes(packet);

                string decryptedClientPacketBuffer;
                try
                {
                    decryptedClientPacketBuffer = Security.AES256.ManagedNetworkDecrypt(
                        client.WithStateLock(c => c.FirstTimeTCPEncryptionKey),
                        client.WithStateLock(c => c.FirstTimeTCPEncryptionIV),
                        packetBuffer,
                        packetBuffer.Length);
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile($"[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Exception occurred while trying to decrypt first-time client packet.", ex);
                   
                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse("REGISTER_REQUEST");
                    respPacket.AddResponse("Invalid packet.");
                    
                    await SendMessageToClient(client, respPacket);
                    return;
                }

                if (!decryptedClientPacketBuffer.IsStringJson())
                {
                    Logger.LogToFile($"[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Packet is not in JSON format.");
                    
                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse("REGISTER_REQUEST");
                    respPacket.AddResponse("Invalid packet.");
                    
                    await SendMessageToClient(client, respPacket);
                    return;
                }

                RoofNetworkNativeRequestPacket? decryptedClientPacket;
                try
                {
                    decryptedClientPacket = JsonSerializer.Deserialize(decryptedClientPacketBuffer, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
                }
                catch(Exception ex)
                {
                    Logger.LogExceptionToFile($"[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Failed to deserialize decrypted packet from the client.", ex);

                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse("REGISTER_REQUEST");
                    respPacket.AddResponse("Invalid packet.");
                    
                    await SendMessageToClient(client, respPacket);
                    return;
                }

                var config = ConfigurationManager.GetConfiguration();
                
                //If the sent back packet is a registration request
                if (decryptedClientPacket != null && decryptedClientPacket.Requests?.Count == 1 &&
                    decryptedClientPacket.Requests[0] == "REGISTER REQUEST" &&
                    SecurityManager.IsValidClientID(decryptedClientPacket.ClientID) &&
                    string.Equals(decryptedClientPacket.ClientID, client.RegistrationClientID, StringComparison.Ordinal) &&
                    !config.RegisteredClients.ContainsKey(decryptedClientPacket.ClientID))
                {
                    string clientIdentifier = decryptedClientPacket.ClientID;

                    Logger.LogToFile($"[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Verified client [{clientIdentifier}]. Registering the client into the database...");

                    bool wasSetupComplete = config.HasCompletedInitialSetup;
                    var encryption = client.WithStateLock(c => (c.FirstTimeTCPEncryptionKey, c.FirstTimeTCPEncryptionIV));
                    if (!await ConfigurationManager.SaveRoofClientNetworkEncryptionKey(clientIdentifier,
                        encryption.FirstTimeTCPEncryptionKey, encryption.FirstTimeTCPEncryptionIV))
                    {
                        Logger.LogToFile("[ClientManager]: Failed to persist client registration.", LogSeverity.Danger);
                        File.Delete(ConfigurationManager.ClientKeyPath(clientIdentifier));
                        throw new IOException("Failed to save client key.");
                    }

                    config.RegisteredClients.Add(clientIdentifier, new RoofClientConfig { AccessCount = 1 });
                    if (!client.IsAdditionalRegistration)
                        config.HasCompletedInitialSetup = true;
                    if (!await ConfigurationManager.SaveConfiguration(config))
                    {
                        Logger.LogToFile("[ClientManager]: Failed to persist client registration.", LogSeverity.Danger);
                        config.RegisteredClients.Remove(clientIdentifier);
                        config.HasCompletedInitialSetup = wasSetupComplete;
                        File.Delete(ConfigurationManager.ClientKeyPath(clientIdentifier));
                        throw new IOException("Failed to save client registration.");
                    }
                    Logger.LogToFile("[ClientManager]: Client registration saved successfully.", LogSeverity.Neutral);
                    if (client.WithStateLock(c => c.IsDisposed))
                        return;
                    client.WithStateLock(c =>
                    {
                        c.ClientID = clientIdentifier;
                        c.IsVerified = true;
                    });

                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = true;
                    respPacket.AddResponse($"RFCLIENT {clientIdentifier} REGISTER SUCCESS");
                    
                    await SendMessageToClient(client, respPacket);

                    client.WithStateLock(c =>
                    {
                        c.IsFirstTimeTCPEncryptionInfoSent = false;
                        c.RegistrationClientID = null;
                        c.FirstTimeTCPEncryptionKey = null;
                        c.FirstTimeTCPEncryptionIV = null;
                    });

                    RemoveFirstTimeUnverifiedClient(client);
                }
                else
                {
                    //Possible sus packet. Drop it.
                    Logger.LogToFile("[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Client TCP encryption verification failed. Dropped the packet from the client.", LogSeverity.Danger);
                    
                    var respPacket = new RoofNetworkNativeResponsePacket();
                    respPacket.Status = false;
                    respPacket.AddResponse("REGISTER_REQUEST");
                    respPacket.AddResponse("Invalid packet.");
                    
                    await SendMessageToClient(client, respPacket);
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ClientManager] - HandleFirstTimeClientTCPEncryptionVerification(): Exception occurred while trying to verify first-time client tcp encryption.", ex);
                
                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("REGISTER_REQUEST");
                respPacket.AddResponse(ex.Message);
                    
                await SendMessageToClient(client, respPacket);
            }
            finally
            {
                if (!client.WithStateLock(c => c.IsVerified))
                    RemoveConnectedClient(client);
                _registrationGate.Release();
            }
        }

        public static async Task SendMessageToClient(RoofClient client, RoofNetworkNativeResponsePacket message)
        {
            try
            {
                var encryptionInfo = await client.GetEncryptionInfo();
                if (encryptionInfo.Item1 == null && encryptionInfo.Item2 == null)
                {
                    Logger.LogToFile("[ClientManager] - SendMessageToClient(): Failed to get encryption info.", LogSeverity.Danger);
                    return;
                }

                var clientTransport = client.WithStateLock(c => (c.IsInRemoteMode, c.EndPoint, c.ClientID));

                if(!clientTransport.IsInRemoteMode)
                    await SendMessageToTCPClient(clientTransport.EndPoint, JsonSerializer.Serialize(message, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket), encryptionInfo.Item1, encryptionInfo.Item2);
                else
                    await SendMessageToWebSocketClient(clientTransport.ClientID, JsonSerializer.Serialize(message, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket), encryptionInfo.Item1, encryptionInfo.Item2);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ClientManager] - SendMessageToClient(): Exception occurred while trying to send a message packet to a client.", ex);
            }
        }

        private static async Task SendMessageToTCPClient(IPEndPoint endPoint, string message, string encryptionKey, string encryptionIV)
        {
            try
            {
                var encryptedMessage = Security.AES256.ManagedNetworkEncrypt(
                    encryptionKey,
                    encryptionIV,
                    message);

                await _networkMessageProvider.SendMessage(encryptedMessage, endPoint);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManager] - SendMessageToTCPClient(): Exception occurred while trying to send a message packet to a client.", ex);
            }
        }
        
        private static async Task SendMessageToWebSocketClient(string forwardingToID, string message, string encryptionKey, string encryptionIV)
        {
            try
            {
                var config = ConfigurationManager.GetConfiguration();
                
                var encryptedMessage = Security.AES256.ManagedNetworkEncrypt(
                    encryptionKey,
                    encryptionIV,
                    message);

                RoofWebSocketPacket wsPacket = new RoofWebSocketPacket()
                {
                    SenderIdentifier = config.RoofIdentifier,
                    ForwardingToIdentifier = forwardingToID,
                    Payload = Encoding.UTF8.GetString(encryptedMessage)
                };
                
                var serializedWSPacket = JsonSerializer.Serialize(wsPacket, RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket);
                var wsPacketBuffer = Encoding.UTF8.GetBytes(serializedWSPacket);

                var provider = _remoteNetworkMessageProvider;
                if (provider != null)
                    await provider.SendMessage(wsPacketBuffer);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManager] - SendMessageToWebSocketClient(): Exception occurred while trying to send a message packet to a client.", ex);
            }
        }
    }
}
