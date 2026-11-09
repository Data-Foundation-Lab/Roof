using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Net;
using Roof.Common.Networking;
using Roof.Common.Tools;
using System.Runtime.InteropServices.Marshalling;
using System.Text.RegularExpressions;
using System.Text.Json;
using System;
using System.Net.Http;
using Roof.Common.Networking.RNMS;
using System.IO;
using System.Diagnostics;
using Roof.Common.Networking.RNMS.WebSocket;

namespace Roof.Client
{
    public class EntryPoint
    {
        internal const int DEFAULT_LOCAL_TCP_CLIENT_PORT = 8671;

        internal static string? _encryptionKey;
        internal static string? _encryptionIV;

        public static NetworkMessageProvider? LocalNetworkMessageProvider = null;
        public static NetworkMessageProvider? PathFinderNetworkMessageProvider = null;
        
        public delegate void CallbackTemplate(RoofNetworkNativeResponsePacket respPacket);
        public static event CallbackTemplate? RoofClientRegistrationCallback;
        public static event CallbackTemplate? RoofCommandExecutionCallback;

        public static void InvokeRoofClientRegistrationCallbackSubscribers(RoofNetworkNativeResponsePacket respPacket) => RoofClientRegistrationCallback?.Invoke(respPacket);
        public static void InvokeRoofCommandExecutionCallbackSubscribers(RoofNetworkNativeResponsePacket respPacket) => RoofCommandExecutionCallback?.Invoke(respPacket);

        public static async Task<RoofNetworkNativeResponsePacket> Initialize()
        {
            try
            {
                RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket() { Status = true };

                if (RoofClientRegistrationCallback == null || RoofCommandExecutionCallback == null)
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Callbacks not set properly.");

                    return respPacket;
                }
                
                if (!RoofClientRegistrationCallback.GetInvocationList().Any())
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Client Registration Callback has no target set.");

                    return respPacket;
                }

                if (!RoofCommandExecutionCallback.GetInvocationList().Any())
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Command Execution Callback has no target set.");

                    return respPacket;
                }

                return respPacket;
            }
            catch (Exception ex)
            {
                RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket() { Status = false };
                respPacket.AddResponse($"Exception occurred while trying to initialize the client libary. Exception: {ex.Message}");

                return respPacket;
            }
        }

        public static void SetEncryptionInfo(string key, string iv)
        {
            if (IsCorrectEncryptionInfoFormat(key, iv))
            {
                _encryptionKey = key;
                _encryptionIV = iv;
            }
        }

        internal static void DisposeEncryptionInfo()
        {
            _encryptionKey = null;
            _encryptionIV = null;
        }
        
        private static bool IsEncryptionInfoSet() => !string.IsNullOrEmpty(_encryptionKey) &&  !string.IsNullOrEmpty(_encryptionIV);
        private static bool IsCorrectEncryptionInfoFormat(string key, string iv)
        {
            try
            {
                byte[] keyBytes = Convert.FromBase64String(key);
                byte[] ivBytes = Convert.FromBase64String(iv);

                return keyBytes.Length == 32 && ivBytes.Length == 16;
            }
            catch
            {
                return false;
            }
        }
        
        public static void Dispose()
        {
            try
            {
                //Unregister all callbacks and dispose all persistent memory objects.

                if (RoofClientRegistrationCallback != null)
                {
                    foreach (var subscriber in RoofClientRegistrationCallback.GetInvocationList())
                    {
                        RoofClientRegistrationCallback -= (CallbackTemplate)subscriber;
                    }
                }

                if(RoofCommandExecutionCallback != null)
                {
                    foreach (var subscriber in RoofCommandExecutionCallback.GetInvocationList())
                    {
                        RoofCommandExecutionCallback -= (CallbackTemplate)subscriber;
                    }
                }

                DisposeNetworkMessageProvider();
                DisposeEncryptionInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roof.Client]: Exception occurred while trying to dispose the client libary. Exception: {ex.Message}");
            }
        }

        public static async Task<RoofNetworkNativeResponsePacket> PresenceBeam()
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

            var activeIPs = await NetworkManager.GetAllLocalNetworkDeviceIPAddresses();

            //If there are no network devices found
            if (activeIPs.Count == 0)
            {
                respPacket.Status = false;
                respPacket.AddResponse("Failed to find any active network devices.");

                return respPacket;
            }

            //If we only found one network device
            //Get the IPv4 address and last 3 octet to check if it's 255 (broadcast address)
            if (activeIPs.Count == 1 && activeIPs[0].AddressFamily == AddressFamily.InterNetwork)
            {
                var ipOctets = activeIPs[0].ToString().Split('.');

                if (ipOctets.Length == 4 && ipOctets[3] == "255")
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Found broadcast address as the only active network device.");

                    return respPacket;
                }
            }

            //Create our own UDP client
            using (UdpClient udpClient = new UdpClient(6968))
            {
                udpClient.EnableBroadcast = true;

                const string presenceBeamStrip = "ROOF_PRESENCE_BEAM"; //local const
                byte[] presenceBeamStripBytes = Encoding.ASCII.GetBytes(presenceBeamStrip);

                var roofIP = IPAddress.None;

                //For each network device found, send off a presence beam at port 6969.
                foreach (var deviceIP in activeIPs)
                {
                    try
                    {
                        IPEndPoint deviceIPEndPoint = new IPEndPoint(deviceIP, 6969);
                        await udpClient.SendAsync(presenceBeamStripBytes, presenceBeamStripBytes.Length, deviceIPEndPoint);
                    }
                    catch (SocketException socketEx)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }

                try
                {
                    TimeSpan overallTimeout = TimeSpan.FromSeconds(10);
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    while (stopwatch.Elapsed < overallTimeout)
                    {
                        TimeSpan remainingTime = overallTimeout - stopwatch.Elapsed;
                        //Console.WriteLine($"DEBUG NATIVE: {remainingTime.Seconds}");

                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1))) // Per iteration timeout
                        {
                            try
                            {
                                Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();
                                UdpReceiveResult result = await receiveTask.WaitAsync(cts.Token); // Enforce timeout

                                string receivedData = Encoding.ASCII.GetString(result.Buffer);
                                if (receivedData.Equals("ROOF_PRESENCE_BEAM_ALIVE", StringComparison.Ordinal))
                                {
                                    roofIP = result.RemoteEndPoint.Address;
                                    break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                //Console.WriteLine("DEBUG NATIVE: Receive Timeout, retrying...");
                                continue;
                            }
                            catch (SocketException socketEx)
                            {
                                //Console.WriteLine($"Socket error: {socketEx.Message}");
                                //break; // Stop execution to avoid infinite failures
                                continue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Console.WriteLine($"Unexpected error: {ex.Message}");
                }

                //If we were unable to find a local active Roof...
                if (roofIP == IPAddress.None)
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Failed to find local active Roof.");

                    return respPacket;
                }
                else //If we successfully found a local active Roof
                {
                    respPacket.Status = true;
                    respPacket.AddResponse(roofIP.ToString());

                    return respPacket;
                }
            }
        }

        public static async Task<NetworkMessageProvider> InitializeNetworkMessageProvider(string destinationAddress)
        {
            if (LocalNetworkMessageProvider == null)
            {
                LocalNetworkMessageProvider = new NetworkMessageProvider();
                await LocalNetworkMessageProvider.Initialize(RNMSNetworkProtocols.MANAGED_TCP, false, destinationAddress, DEFAULT_LOCAL_TCP_CLIENT_PORT);

                LocalNetworkMessageProvider.OnMessageReceived += ClientNetworkMessageProvider_OnMessageReceived;
                LocalNetworkMessageProvider.OnClientDisconnected += ClientNetworkMessageProvider_OnClientDisconnected;
            }

            return LocalNetworkMessageProvider;
        }

        public static void SetPathFinderNetworkMessageProvider(NetworkMessageProvider pathfinderNMP)
        {
            PathFinderNetworkMessageProvider = pathfinderNMP;
        }

        internal static void ClientNetworkMessageProvider_OnMessageReceived(IPEndPoint? clientAddress, string message)
        {
            HandleCommandExecutionResultPacket(message);
        }

        private static void ClientNetworkMessageProvider_OnClientDisconnected(object? sender, IPEndPoint e)
        {
            DisposeNetworkMessageProvider();
        }

        private static void DisposeNetworkMessageProvider()
        {
            if (LocalNetworkMessageProvider != null)
            {
                LocalNetworkMessageProvider.OnMessageReceived -= ClientNetworkMessageProvider_OnMessageReceived;
                LocalNetworkMessageProvider.OnClientDisconnected -= ClientNetworkMessageProvider_OnClientDisconnected;
                LocalNetworkMessageProvider.Dispose();
            }
        }

        public static async Task<RoofNetworkNativeResponsePacket> ExecuteRoofCommand(RoofNetworkNativeRequestPacket requestPacket, bool isThroughPathFinder = false)
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

            if(!IsEncryptionInfoSet())
            {
                respPacket.Status = false;
                respPacket.AddResponse("Encryption info is not set.");

                return respPacket;
            }

            if (requestPacket == null)
            {
                respPacket.Status = false;
                respPacket.AddResponse("Invalid request packet.");

                return respPacket;
            }

            if (string.IsNullOrEmpty(requestPacket.ClientID) || !Security.Is128BitHash(requestPacket.ClientID))
            {
                respPacket.Status = false;
                respPacket.AddResponse("Invalid client ID format.");

                return respPacket;
            }

            if(requestPacket.Requests == null || requestPacket.Requests.Count == 0)
            {
                respPacket.Status = false;
                respPacket.AddResponse("Invalid request format.");

                return respPacket;
            }

            if (string.IsNullOrEmpty(requestPacket.Requests[0]))
            {
                respPacket.Status = false;
                respPacket.AddResponse("Invalid command message.");

                return respPacket;
            }

            if (!isThroughPathFinder && LocalNetworkMessageProvider == null)
            {
                respPacket.Status = false;
                respPacket.AddResponse("Local NMP not initialized.");

                return respPacket;
            }

            if(isThroughPathFinder && PathFinderNetworkMessageProvider == null)
            {
                respPacket.Status = false;
                respPacket.AddResponse("PathFinder NMP not initialized.");

                return respPacket;
            }

            bool networkPacketSendResult = false;

            if(isThroughPathFinder)
            {   
                var serializedReqPacket = JsonSerializer.Serialize(requestPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
                
                var encryptedReqPacket = Security.AES256.ManagedNetworkEncrypt(
                    _encryptionKey,
                    _encryptionIV,
                    serializedReqPacket);
                
                RoofWebSocketPacket packet = new RoofWebSocketPacket()
                {
                    SenderIdentifier = requestPacket.ClientID,
                    ForwardingToIdentifier = requestPacket.RoofIP,
                    Payload = Encoding.UTF8.GetString(encryptedReqPacket)
                };

                var serializedPacket = JsonSerializer.Serialize(packet, RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket);
                var packetBuffer = Encoding.UTF8.GetBytes(serializedPacket);

                networkPacketSendResult = await PathFinderNetworkMessageProvider.SendMessage(packetBuffer);
            }
            else
            {
                var serializedPacket = JsonSerializer.Serialize(requestPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
                var encryptedPacket = Security.AES256.ManagedNetworkEncrypt(
                                      _encryptionKey,
                                      _encryptionIV,
                                      serializedPacket);

                networkPacketSendResult = await LocalNetworkMessageProvider.SendMessage(encryptedPacket);
            }

            if (networkPacketSendResult)
            {
                respPacket.Status = true;

                return respPacket;
            }
            else
            {
                respPacket.Status = false;
                respPacket.AddResponse("Failed to send network packet.");

                return respPacket;
            }
        }

        public static void HandleCommandExecutionResultPacket(string packet)
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

            if (!IsEncryptionInfoSet())
            {
                respPacket.Status = false;
                respPacket.AddResponse($"Encryption info is not set.");

                InvokeRoofCommandExecutionCallbackSubscribers(respPacket);

                return;
            }

            string decryptedRoofCommandResultPacket = "";
            try
            {
                byte[] messageBuffer = Encoding.UTF8.GetBytes(packet);
                decryptedRoofCommandResultPacket = Security.AES256.ManagedNetworkDecrypt(
                    _encryptionKey,
                    _encryptionIV,
                    messageBuffer,
                    messageBuffer.Length);
            }
            catch (Exception ex)
            {
                respPacket.Status = false;
                respPacket.AddResponse($"Error decrypting Roof response packet.");
                respPacket.AddResponse(ex.Message);

                InvokeRoofCommandExecutionCallbackSubscribers(respPacket);

                return;
            }

            try
            {
                var respPacketStruct = JsonSerializer.Deserialize(decryptedRoofCommandResultPacket, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
                respPacket.Status = respPacketStruct.Status;
                respPacket.Responses = respPacketStruct.Responses;

                InvokeRoofCommandExecutionCallbackSubscribers(respPacket);

                return;
            }
            catch (Exception ex)
            {
                respPacket.Status = false;
                respPacket.AddResponse($"Error converting Roof command result packet.");
                respPacket.AddResponse(ex.Message);

                InvokeRoofCommandExecutionCallbackSubscribers(respPacket);

                return;
            }
        }
    }
}
