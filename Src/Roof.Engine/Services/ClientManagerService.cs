using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Roof.Engine.Tools;
using Roof.Common.Networking;
using Roof.Engine.Managers;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net.Http;
using static Roof.Engine.Managers.ClientManager;
using System.Buffers;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using Roof.Common.Tools;
using Roof.Common.Networking.RNMS;
using Roof.Common.Networking.RNMS.WebSocket;
using Roof.Engine.Structs;

namespace Roof.Engine.Services
{
    internal class ClientManagerService : ServiceBase
    {
        private const int SETUPAP_DETECTION_WINDOW_TICKRATE_MILLISECONDS = 15000; //Running every 15 seconds
        
        private CancellationTokenSource _packetHandlerCts = new CancellationTokenSource();
        private readonly CancellationTokenSource _SetupAPDetectionHandlerCts = new();
        
        private NetworkMessageProvider _localNetworkMessageProvider = new NetworkMessageProvider();
        
        private Task _presenceBeamTask = Task.CompletedTask;
        private Task _tcpListenerTask = Task.CompletedTask;
        private Task _setupAPTask = Task.CompletedTask;

        internal override Task Initialize()
        {
            return Task.CompletedTask;
        }

        internal override async Task<bool> Terminate()
        {
            _packetHandlerCts.Cancel();
            _SetupAPDetectionHandlerCts.Cancel();
            await _tcpListenerTask;
            _localNetworkMessageProvider.OnClientConnected -= _localNetworkMessageProvider_OnClientConnected;
            _localNetworkMessageProvider.OnClientDisconnected -= _localNetworkMessageProvider_OnClientDisconnected;
            _localNetworkMessageProvider.OnMessageReceived -= _localNetworkMessageProvider_OnMessageReceived;
            _localNetworkMessageProvider.Dispose();
            foreach (var client in ClientManager.GetConnectedClientsSnapshot())
                if (!client.IsInRemoteMode)
                    ClientManager.RemoveConnectedClient(client);

            await Task.WhenAll(_presenceBeamTask, _setupAPTask);
            return true;
        }

        internal override void Start()
        {
            Logger.LogToFile("[ClientManagerService]: Monitoring client connections, packets, and setup AP hosting requirement...");

            _presenceBeamTask = Task.Run(RunPresenceBeamHandler);
            _tcpListenerTask = Task.Run(RunLocalClientTCPConnectionListener);
            _setupAPTask = Task.Run(RunSetupAPRequirementDetectionHandler);
            ShutdownManager.Observe(_presenceBeamTask);
        }

        private async Task RunPresenceBeamHandler()
        {
            try
            {
                using (UdpClient udpServer = new UdpClient(ClientManager.LOCAL_PRESENCE_BEAM_UDP_PORT))
                {
                    while (!_packetHandlerCts.IsCancellationRequested)
                    {
                        UdpReceiveResult result = await udpServer.ReceiveAsync(_packetHandlerCts.Token);

                        string receivedData = Encoding.ASCII.GetString(result.Buffer);
                        IPEndPoint clientEndPoint = result.RemoteEndPoint;

                        if (receivedData.Contains("ROOF_PRESENCE_BEAM"))
                        {
                            Logger.LogToFile($"[ClientManagerService]: Received a presence beam from {clientEndPoint.Address}.");

                            byte[] responseBytes = Encoding.ASCII.GetBytes("ROOF_PRESENCE_BEAM_ALIVE");
                            await udpServer.SendAsync(responseBytes, responseBytes.Length, clientEndPoint);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_packetHandlerCts.IsCancellationRequested)
            {
            }
        }

        private async Task RunLocalClientTCPConnectionListener()
        {
            try
            {
                _localNetworkMessageProvider.OnClientConnected += _localNetworkMessageProvider_OnClientConnected;
                _localNetworkMessageProvider.OnClientDisconnected += _localNetworkMessageProvider_OnClientDisconnected;
                _localNetworkMessageProvider.OnMessageReceived += _localNetworkMessageProvider_OnMessageReceived;

                ClientManager.SetNetworkMessageProvider(_localNetworkMessageProvider);
                bool initialized = await _localNetworkMessageProvider.Initialize(RNMSNetworkProtocols.MANAGED_TCP, true, IPAddress.Any.ToString(), DEFAULT_LOCAL_CLIENT_TCP_PORT);

                Logger.LogToFile(initialized
                    ? $"[ClientManagerService]: Listening for local TCP clients on port: {ClientManager.DEFAULT_LOCAL_CLIENT_TCP_PORT}."
                    : "[ClientManagerService]: Failed to initialize local TCP listener.",
                    initialized ? LogSeverity.Neutral : LogSeverity.Danger);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientManagerService] - RunLocalClientTCPConnectionListener(): Exception occurred while trying start local client TCP connection listener.", ex);
            }
        }

        private async Task RunSetupAPRequirementDetectionHandler()
        {
            try
            {
                using var timer = new PeriodicTimer(
                    TimeSpan.FromMilliseconds(SETUPAP_DETECTION_WINDOW_TICKRATE_MILLISECONDS));

                while (await timer.WaitForNextTickAsync(_SetupAPDetectionHandlerCts.Token))
                {
                    try
                    {
                        ClientManager.RemoveStaleClients();
                        await SetupAPManager.UpdateSetupAPRequirement();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogExceptionToFile(
                            "[ClientManagerService]: Exception occurred while evaluating setup AP hosting requirement.",
                            ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        private void _localNetworkMessageProvider_OnClientConnected(object? sender, IPEndPoint clientEndPoint)
        {
            if (_packetHandlerCts.IsCancellationRequested)
                return;
            if (ClientManager.TryGetConnectedClientByEndpoint(clientEndPoint, out var previousClient))
                ClientManager.RemoveConnectedClient(previousClient);
            if (ClientManager.TryAddConnectedClient(new RoofClient { EndPoint = clientEndPoint }))
            {
                Logger.LogToFile($"[ClientManagerService]: Client {clientEndPoint} connected.");
            }
        }

        private void _localNetworkMessageProvider_OnClientDisconnected(object? sender, IPEndPoint clientEndPoint)
        {
            if (ClientManager.TryGetConnectedClientByEndpoint(clientEndPoint, out var foundClient))
            {
                Logger.LogToFile($"[ClientManagerService]: Client {clientEndPoint} disconnected.");
                ClientManager.RemoveConnectedClient(foundClient);
            }
        }

        private void _localNetworkMessageProvider_OnMessageReceived(IPEndPoint? clientEndPoint, string packetString)
        {
            ShutdownManager.RunClientWork(() => HandleMessageReceived(clientEndPoint, packetString));
        }

        private async Task HandleMessageReceived(IPEndPoint? clientEndPoint, string packetString)
        {
#if DEBUG
            Logger.Log($"_localNetworkMessageProvider_OnMessageReceived => {packetString}");
#endif

            if (_packetHandlerCts.IsCancellationRequested || !ClientManager.TryGetConnectedClientByEndpoint(clientEndPoint, out var client))
                return;

            if (!packetString.IsStringJson())
            {
                if (ClientManager.TryGetFirstTimeUnverifiedClientByEndpoint(clientEndPoint, true, out var registeringClient))
                    await ClientManager.HandleFirstTimeClientTCPEncryptionVerification(_localNetworkMessageProvider, registeringClient, packetString);
                else
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(packetString);
                    await ClientManager.HandleClientCommandPacket(client, buffer, buffer.Length);
                }
                return;
            }

            try
            {
                var packet = JsonSerializer.Deserialize(packetString,
                    RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
                var config = ConfigurationManager.GetConfiguration();
                if (packet == null || !SecurityManager.IsValidClientID(packet.ClientID) ||
                    packet.Requests == null || packet.Requests.Count != 1 ||
                    packet.Requests[0] != "TCP INFO REQUEST" ||
                    config.RegisteredClients.ContainsKey(packet.ClientID) ||
                    (!config.HasCompletedInitialSetup && config.RegisteredClients.Count != 0))
                {
                    return;
                }

                await ClientManager.HandleFirstTimeClientTCPInfoRequest(_localNetworkMessageProvider, client, packet.ClientID, config);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile(ConfigurationManager.GetConfiguration().HasCompletedInitialSetup
                    ? "[ClientManagerService]: Exception occurred while trying deserialize trial packet."
                    : "[ClientManagerService]: Exception occurred while trying to deserialize client packet.", ex);
            }
        }
        
    }
}
