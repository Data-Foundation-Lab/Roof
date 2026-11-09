using Roof.Common.Networking.RNMS;
using Roof.Common.Networking.RNMS.WebSocket;
using Roof.Common.Tools;
using Roof.Engine.Managers;
using Roof.Engine.Tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Roof.Engine.Structs;

namespace Roof.Engine.Services
{
    internal class PathFinderService : ServiceBase
    {
        private const string PATHFINDER_URL = "PATHFINDER_PUBLIC_URL";
        private const int MAX_CLIENT_PAYLOAD_BYTES = 16 * 1024 * 1024;
        
        private NetworkMessageProvider? _pfNetworkMessageProvider;
        
        private readonly object _pfLock = new();
        private CancellationTokenSource _pfCts = new();
        
        private readonly TimeSpan _disconnectionTimeout = TimeSpan.FromSeconds(15);
        private readonly TimeSpan _defaultWSHeartbeatRecurrenceTime = TimeSpan.FromSeconds(5);

        private volatile bool _isConnectedToPathFinder;
        private volatile bool _isPFDisposed;

        private Task[] _backgroundTasks = [];

        internal override Task Initialize()
        {
            return Task.CompletedTask;
        }

        internal override async Task<bool> Terminate()
        {
            _isPFDisposed = true;                              
            _pfCts.Cancel();                                   
            _isConnectedToPathFinder = false;
            
            ReplaceProvider(null);

            await Task.WhenAll(_backgroundTasks);
            return true;
        }
        
        internal override void Start()
        {
            _backgroundTasks =
            [
                Task.Run(RunRemoteClientWebSocketConnectionListener),
                Task.Run(SendPFHeartbeatForClients),
                Task.Run(MonitorDisconnectionForClients)
            ];
            foreach (var task in _backgroundTasks)
                ShutdownManager.Observe(task);
        }
        
        private async Task RunRemoteClientWebSocketConnectionListener()
        {
            // backoff settings
            var rng = new Random();
            TimeSpan delay;
            var reconnectDelay = TimeSpan.FromSeconds(20);
            
            while (!_pfCts.IsCancellationRequested && !_isPFDisposed)
            {
                NetworkMessageProvider? provider = null;
                try
                {
                    // Ensure any previous provider is torn down before creating a new one
                    ReplaceProvider(null);

                    provider = new NetworkMessageProvider();
                    provider.OnWebSocketClientConnected += _pathFinderNetworkMessageProvider_OnClientConnected;
                    provider.OnWebSocketClientDisconnected += _pathFinderNetworkMessageProvider_OnClientDisconnected;
                    provider.OnWebSocketMessageReceived += _pathFinderNetworkMessageProvider_OnMessageReceived;

                    // Keep the pending provider available for shutdown, but do not publish it.
                    lock (_pfLock)
                    {
                        if (_isPFDisposed)
                        {
                            DisposeProvider(provider);
                            break;
                        }
                        _pfNetworkMessageProvider = provider;
                    }

                    Logger.LogToFile("[PathFinderService]: Connecting to PathFinder...");
                    bool providerInitialized = await provider.Initialize(RNMSNetworkProtocols.WEBSOCKET_TCP, false, PATHFINDER_URL, 0, _pfCts.Token);
                    if (!providerInitialized)
                    {
                        Logger.LogToFile(
                            "[PathFinderService]: PathFinder is unavailable. Connection will be retried.",
                            LogSeverity.Warning);
                    }
                    else
                    {
                        // Publish the provider so other components can use it
                        lock (_pfLock)
                        {
                            if (_isPFDisposed || !_isConnectedToPathFinder ||
                                provider.GetProvider() is not WebSocketProvider socket || !socket.IsConnected)
                                throw new InvalidOperationException("PathFinder disconnected during initialization.");
                            ClientManager.SetRemoteNetworkMessageProvider(provider);
                        }

                        Logger.LogToFile("[PathFinderService]: WebSocket NetworkMessageProvider initialized");

                        // Give the connection event time to fire
                        var connectionGraceDeadline = DateTime.UtcNow.AddSeconds(10);

                        while (!_isConnectedToPathFinder &&
                               DateTime.UtcNow < connectionGraceDeadline &&
                               !_isPFDisposed &&
                               !_pfCts.IsCancellationRequested)
                        {
                            await Task.Delay(250, _pfCts.Token).ConfigureAwait(false);
                        }
                    
                        if (!_isConnectedToPathFinder)
                        {
                            Logger.LogToFile("[PathFinderService]: PathFinder connection did not become active within grace period.");
                            ReplaceProvider(null);
                        }
                        else
                        {
                            while (!_isPFDisposed && !_pfCts.IsCancellationRequested)
                            {
                                if (!_isConnectedToPathFinder || provider.GetProvider() is not WebSocketProvider socket || !socket.IsConnected)
                                    break;

                                await Task.Delay(2000, _pfCts.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // graceful shutdown
                }
                catch (Exception ex)
                {
                    Logger.LogToFile(
                        $"[PathFinderService]: PathFinder connection failed ({ex.GetType().Name}). Connection will be retried.",
                        LogSeverity.Warning);
                }
                finally
                {
                    ReplaceProvider(null);
                    // Shutdown may have cleared the pending reference during initialization.
                    if (provider != null)
                        DisposeProvider(provider);
                }

                // Reconnect with fixed delay + jitter
                if (_pfCts.IsCancellationRequested || _isPFDisposed)
                    break;

                delay = NextReconnectDelay(reconnectDelay, rng);
                Logger.LogToFile($"[PathFinderService]: Attempting PathFinder reconnection in {delay.TotalSeconds:F0}s...");
                try
                {
                    await Task.Delay(delay, _pfCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }

            // Final cleanup on exit
            ReplaceProvider(null);
            Logger.LogToFile("[PathFinderService]: PathFinder connection loop stopped.");
        }
        
        private static TimeSpan NextReconnectDelay(TimeSpan baseDelay, Random rng)
        {
            var jitter = baseDelay.TotalMilliseconds * (0.2 * rng.NextDouble());
            return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds + jitter);
        }

        /// <summary>
        /// Swap the active provider atomically and clean up the previous connection.
        /// </summary>
        private void ReplaceProvider(NetworkMessageProvider? newProvider)
        {
            NetworkMessageProvider? old;
            lock (_pfLock)
            {
                old = _pfNetworkMessageProvider;
                _pfNetworkMessageProvider = newProvider;
                if (newProvider == null)
                {
                    _isConnectedToPathFinder = false;
                    ClientManager.SetRemoteNetworkMessageProvider(null);
                    foreach (var client in ClientManager.GetConnectedClientsSnapshot())
                        if (client.WithStateLock(c => c.IsInRemoteMode))
                            ClientManager.RemoveConnectedClient(client);
                }
            }

            if (old != null)
                DisposeProvider(old);
        }

        private void DisposeProvider(NetworkMessageProvider provider)
        {
            provider.OnWebSocketClientConnected -= _pathFinderNetworkMessageProvider_OnClientConnected;
            provider.OnWebSocketClientDisconnected -= _pathFinderNetworkMessageProvider_OnClientDisconnected;
            provider.OnWebSocketMessageReceived -= _pathFinderNetworkMessageProvider_OnMessageReceived;
            try { provider.Dispose(); } catch { }
        }

        private void _pathFinderNetworkMessageProvider_OnClientConnected(object? sender, string e)
        {
            lock (_pfLock)
            {
                if (_isPFDisposed || !ReferenceEquals(_pfNetworkMessageProvider?.GetProvider(), sender))
                    return;
                // Set ClientID once we have an underlying socket, before heartbeat tasks start.
                ((WebSocketProvider)sender!).SetClientID(ConfigurationManager.GetConfiguration().RoofIdentifier);
                _isConnectedToPathFinder = true;
            }
            Logger.LogToFile("[PathFinderService]: Successfully connected to PathFinder.");
        }

        private void _pathFinderNetworkMessageProvider_OnClientDisconnected(object? sender, string e)
        {
            lock (_pfLock)
            {
                if (!ReferenceEquals(_pfNetworkMessageProvider?.GetProvider(), sender))
                    return;
                _isConnectedToPathFinder = false;
                ClientManager.SetRemoteNetworkMessageProvider(null);
                foreach (var client in ClientManager.GetConnectedClientsSnapshot())
                    if (client.WithStateLock(c => c.IsInRemoteMode))
                        ClientManager.RemoveConnectedClient(client);
            }
            Logger.LogToFile("[PathFinderService]: Disconnected from PathFinder.");
        }

        private void _pathFinderNetworkMessageProvider_OnMessageReceived(RoofWebSocketPacket packet)
        {
            ShutdownManager.RunClientWork(() => HandleMessageReceived(packet));
        }

        private async Task HandleMessageReceived(RoofWebSocketPacket packet)
        {
            try
            {
                var config = ConfigurationManager.GetConfiguration();
                if (packet == null || config?.RegisteredClients == null ||
                    string.IsNullOrWhiteSpace(packet.SenderIdentifier) ||
                    string.IsNullOrWhiteSpace(packet.SenderFingerprint) ||
                    string.IsNullOrEmpty(packet.Payload) ||
                    !string.Equals(packet.ForwardingToIdentifier, config.RoofIdentifier, StringComparison.Ordinal) ||
                    !config.RegisteredClients.ContainsKey(packet.SenderIdentifier))
                {
                    Logger.LogToFile("[PathFinderService]: Rejected invalid or unregistered remote client packet.", LogSeverity.Warning);
                    return;
                }

                // Enforce the Engine's payload limit before creating client state or decrypting.
                if (packet.Payload.Length > MAX_CLIENT_PAYLOAD_BYTES ||
                    Encoding.UTF8.GetByteCount(packet.Payload) > MAX_CLIENT_PAYLOAD_BYTES)
                {
                    Logger.LogToFile("[PathFinderService]: Rejected oversized remote client packet.", LogSeverity.Warning);
                    return;
                }

#if DEBUG
                Logger.Log($"[PathFinderService]: _pathFinderNetworkMessageProvider_OnMessageReceived() => {packet.Payload}");
#endif

                if (packet.Payload == "TCP INFO REQUEST" || packet.Payload == "REGISTER REQUEST" || packet.Payload.IsStringJson())
                {
                    Logger.LogToFile("[PathFinderService]: A remote mode client attempted to register over PathFinder. Rejecting request...");
                    return;
                }

                //Handle potential command packet.
                RoofClient commandingClient;
                lock (_pfLock)
                {
                    if (!_isConnectedToPathFinder || _isPFDisposed)
                        return;
                    if (packet.Payload == "PF_HEARTBEAT")
                    {
                        var existingClient = ClientManager.GetConnectedClientsSnapshot().FirstOrDefault(c =>
                            c.WithStateLock(state => state.IsInRemoteMode && state.IsVerified &&
                                state.ClientID == packet.SenderIdentifier && state.GlobalRoutedFingerprint == packet.SenderFingerprint));
                        existingClient?.WithStateLock(c => c.LastPFHeartbeatReceivedTime = DateTime.UtcNow);
                        return;
                    }
                    commandingClient = ClientManager.GetOrAddRemoteClient(packet.SenderIdentifier, packet.SenderFingerprint);
                }

                byte[] clientPacketBuffer = Encoding.UTF8.GetBytes(packet.Payload);
                await ClientManager.HandleClientCommandPacket(commandingClient, clientPacketBuffer, clientPacketBuffer.Length);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[PathFinderService]: Dropped invalid client packet ({ex.GetType().Name}).", LogSeverity.Warning);
            }
        }

        private async Task MonitorDisconnectionForClients()
        {
            try
            {
                while (!_pfCts.IsCancellationRequested)
                {
                    if (!_isConnectedToPathFinder)
                    {
                        await Task.Delay(500, _pfCts.Token);
                        continue;
                    }
                    
                    foreach (var rfClient in ClientManager.GetConnectedClientsSnapshot())
                    {
                        // Check the heartbeat deadline, including when no first heartbeat arrives.
                        if (rfClient.WithStateLock(c => c.IsInRemoteMode && c.LastPFHeartbeatReceivedTime != null && DateTime.UtcNow - c.LastPFHeartbeatReceivedTime > _disconnectionTimeout))
                        {
                            // Remove stale client state so a reconnect starts a fresh session.
                            ClientManager.RemoveConnectedClient(rfClient);
                            Logger.LogToFile($"[PathFinderService]: Client {rfClient.ClientID}({rfClient.GlobalRoutedFingerprint}) disconnected.");
                            continue;
                        }
                    }

                    await Task.Delay(1000, _pfCts.Token);
                }
            }
            catch (TaskCanceledException) { }
        }
        
        private async Task SendPFHeartbeatForClients()
        {
            try
            {
                while(!_pfCts.Token.IsCancellationRequested)
                {
                    NetworkMessageProvider? provider;
                    lock (_pfLock)
                    {
                        provider = _pfNetworkMessageProvider;
                    }

                    if (!_isConnectedToPathFinder || provider?.GetProvider() is not WebSocketProvider ws)
                    {
                        await Task.Delay(500, _pfCts.Token);
                        continue;
                    }
                    
                    foreach (var rfClient in ClientManager.GetConnectedClientsSnapshot())
                    {
                        if(rfClient.WithStateLock(c => c.IsInRemoteMode && (c.LastPFHeartbeatSentTime == null || DateTime.UtcNow - c.LastPFHeartbeatSentTime > _defaultWSHeartbeatRecurrenceTime)))
                        {
                            var heartbeatPacket = new RoofWebSocketPacket()
                            {
                                SenderIdentifier = ConfigurationManager.GetConfiguration().RoofIdentifier,
                                ForwardingToIdentifier = rfClient.ClientID,
                                Payload = "PF_HEARTBEAT"
                            };
                            var serializedHeartbeat = JsonSerializer.Serialize(heartbeatPacket, RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket);

                            byte[] heartbeatPacketBuffer = Encoding.UTF8.GetBytes(serializedHeartbeat);

                            if (!await ws.SendAsync(heartbeatPacketBuffer, _pfCts.Token))
                                break;

                            rfClient.WithStateLock(c => c.LastPFHeartbeatSentTime = DateTime.UtcNow);
                        }
                    }

                    await Task.Delay(500, _pfCts.Token);
                }
            }
            catch(TaskCanceledException) { }
        }
    }
}
