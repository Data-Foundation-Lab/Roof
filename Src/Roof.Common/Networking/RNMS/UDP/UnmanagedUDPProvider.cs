using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.UDP
{
    internal class UnmanagedUDPProvider : IDisposable
    {
        private const string HEARTBEAT = "UMUDP_HEARTBEAT";
        private const string DATA_PREFIX = "UMUDP_DATA:";

        /// <summary>
        /// Timers to track client lifetime.
        /// </summary>
        private class ClientTimers
        {
            public DateTime? LastMessageReceivedTime { get; set; } = null;
            public DateTime? LastHeatbeatSentTime { get; set; } = null;
            public DateTime? LastHeatbeatReceivedTime { get; set; } = null;
        }

        private readonly UdpClient _udpClient;
        private CancellationTokenSource _cts;

        private IPEndPoint _clientModeDestinationAddress;
        private ClientTimers _clientModeClientTimers;

        private Dictionary<IPEndPoint, ClientTimers> _listenerModeConnectedClients = new Dictionary<IPEndPoint, ClientTimers>();

        private readonly bool _isInListenerMode;
        private bool _isInClientMode => !_isInListenerMode;

        private Task _listeningTask;
        private Task _monitorDisconnectionTask;
        private Task _keepAliveHeartbeatTask;

        public delegate void MessageReceivedEventDelegate(IPEndPoint clientAddress, string message);
        public event MessageReceivedEventDelegate MessageReceived;
        public event EventHandler<IPEndPoint> ClientConnected;
        public event EventHandler<IPEndPoint> ClientDisconnected;
        public event EventHandler SelfDisconnected;

        public int Port
        {
            get
            {
                if (_udpClient == null || _udpClient.Client == null || _udpClient.Client.LocalEndPoint == null)
                    return 0;
                else
                    return ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="destinationAddress">For client mode: the target server. In listener mode, not used.</param>
        /// <param name="destinationPort">For client mode: the target port, or for listener mode the port to bind.</param>
        /// <param name="isInListenerMode">If true, this instance will act as a listener.</param>
        public UnmanagedUDPProvider(string destinationAddress, int destinationPort, bool isInListenerMode)
        {
            _isInListenerMode = isInListenerMode;
            // In listener mode bind to the provided port; otherwise use an OS-assigned port.
            _udpClient = new UdpClient(!isInListenerMode ? 0 : destinationPort);
            _cts = new CancellationTokenSource();

            if (_isInClientMode)
            {
                _clientModeDestinationAddress = new IPEndPoint(IPAddress.Parse(destinationAddress), destinationPort);
                _clientModeClientTimers = new ClientTimers();
            }

            _listeningTask = Task.Run(() => ListenForMessagesAsync(_cts.Token));
            _monitorDisconnectionTask = Task.Run(() => MonitorClientDisconnectionAsync(_cts.Token));
            _keepAliveHeartbeatTask = Task.Run(() => SendHeartbeatAsync(_cts.Token));
        }

        #region Event Invokers
        protected virtual void InvokeOnMessageReceived(IPEndPoint clientAddress, string message) =>
            MessageReceived?.Invoke(clientAddress, message);

        protected virtual void InvokeOnClientConnected(IPEndPoint clientAddress) =>
            ClientConnected?.Invoke(this, clientAddress);

        protected virtual void InvokeOnClientDisconnected(IPEndPoint clientAddress) =>
            ClientDisconnected?.Invoke(this, clientAddress);

        protected virtual void InvokeOnSelfDisconnected() =>
            SelfDisconnected?.Invoke(this, EventArgs.Empty);
        #endregion

        #region Non-Reliable Sending

        /// <summary>
        /// Sends a message without reliability tracking. Useful for heartbeats or other non-critical data.
        /// </summary>
        public async Task<bool> SendMessageUnreliable(string message, IPEndPoint? clientAddress = null)
        {
            // For non-reliable sends, use the provided clientAddress or the default for client mode.
            IPEndPoint targetEndpoint = clientAddress ?? (_isInClientMode ? _clientModeDestinationAddress : throw new InvalidOperationException(RNMSErrorMessages.CLIENT_ADDRESS_REQUIRED_ERROR));
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            await _udpClient.SendAsync(messageBuffer, messageBuffer.Length, targetEndpoint);
            return true;
        }

        /// <summary>
        /// Sends a message without reliability tracking.
        /// </summary>
        public async Task<bool> SendMessageUnreliable(byte[] messageBuffer, IPEndPoint? clientAddress = null)
        {
            IPEndPoint targetEndpoint = clientAddress ?? (_isInClientMode ? _clientModeDestinationAddress : throw new InvalidOperationException(RNMSErrorMessages.CLIENT_ADDRESS_REQUIRED_ERROR));
            await _udpClient.SendAsync(messageBuffer, messageBuffer.Length, targetEndpoint);
            return true;
        }
        #endregion

        private async Task ListenForMessagesAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult udpResult = await _udpClient.ReceiveAsync();
                    IPEndPoint sender = udpResult.RemoteEndPoint;

                    // In client mode, if the sender isn't our target, disconnect.
                    if (_isInClientMode && !sender.Equals(_clientModeDestinationAddress))
                    {
                        SelfDisconnect();
                        return;
                    }

                    // In listener mode, add new clients as they send messages.
                    if (_isInListenerMode && !_listenerModeConnectedClients.ContainsKey(sender))
                    {
                        _listenerModeConnectedClients.Add(sender, new ClientTimers { LastMessageReceivedTime = DateTime.UtcNow });
                        InvokeOnClientConnected(sender);
                    }

                    string message = Encoding.UTF8.GetString(udpResult.Buffer);

                    // Update last received time.
                    if (_isInClientMode)
                    {
                        _clientModeClientTimers.LastMessageReceivedTime = DateTime.UtcNow;
                    }
                    else
                    {
                        if (_listenerModeConnectedClients.ContainsKey(sender))
                        {
                            _listenerModeConnectedClients[sender].LastMessageReceivedTime = DateTime.UtcNow;
                        }
                    }

                    // Handle heartbeat messages.
                    if (message.Equals(HEARTBEAT))
                    {
                        //Console.WriteLine($"Received heartbeat from {sender} at {DateTime.UtcNow}");
                        if (_isInClientMode)
                        {
                            _clientModeClientTimers.LastHeatbeatReceivedTime = DateTime.UtcNow;
                        }
                        else if (_listenerModeConnectedClients.TryGetValue(sender, out ClientTimers clientTimers))
                        {
                            clientTimers.LastHeatbeatReceivedTime = DateTime.UtcNow;
                        }
                        continue; // Skip further processing.
                    }

                    // Process data messages. Expected format: "UMUDP_DATA:{payload}"
                    if (message.StartsWith(DATA_PREFIX, StringComparison.Ordinal))
                    {
                        int messageIndex = message.IndexOf(':');
                        if (messageIndex != -1)
                        {
                            // Extract payload.
                            string payload = message.Substring(messageIndex + 1);

                            if (_isInListenerMode)
                            {
                                InvokeOnMessageReceived(sender, payload);
                            }
                            else
                            {
                                InvokeOnMessageReceived(_clientModeDestinationAddress, payload);
                            }
                        }
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ListenForMessagesAsync: {ex.Message}");
                SelfDisconnect();
            }
        }

        private async Task MonitorClientDisconnectionAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isInListenerMode)
                    {
                        foreach (var kvp in _listenerModeConnectedClients.ToList())
                        {
                            var clientTimers = kvp.Value;
                            DateTime? lastActivity = clientTimers.LastHeatbeatReceivedTime ?? clientTimers.LastMessageReceivedTime;
                            if (lastActivity != null && DateTime.UtcNow - lastActivity > RNMSDefaults.DisconnectionTimeout)
                            {
                                InvokeOnClientDisconnected(kvp.Key);
                                _listenerModeConnectedClients.Remove(kvp.Key);
                            }
                        }
                    }
                    else
                    {
                        DateTime? lastReceived = _clientModeClientTimers.LastHeatbeatReceivedTime ?? _clientModeClientTimers.LastMessageReceivedTime;
                        if (lastReceived != null && DateTime.UtcNow - lastReceived > RNMSDefaults.DisconnectionTimeout)
                        {
                            InvokeOnClientDisconnected(_clientModeDestinationAddress);
                            SelfDisconnect();
                            break;
                        }
                    }
                    await Task.Delay(RNMSDefaults.UdpDisconnectionPollInterval, token);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task SendHeartbeatAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isInListenerMode)
                    {
                        foreach (var client in _listenerModeConnectedClients)
                        {
                            var clientTimers = client.Value;
                            if (clientTimers.LastHeatbeatSentTime == null ||
                                DateTime.UtcNow - clientTimers.LastHeatbeatSentTime > RNMSDefaults.HeartbeatInterval)
                            {
                                // Heartbeat is sent unreliably.
                                await SendMessageUnreliable(HEARTBEAT, client.Key);
                                clientTimers.LastHeatbeatSentTime = DateTime.UtcNow;
                            }
                        }
                    }
                    else
                    {
                        if (_clientModeClientTimers.LastHeatbeatSentTime == null ||
                            DateTime.UtcNow - _clientModeClientTimers.LastHeatbeatSentTime > RNMSDefaults.HeartbeatInterval)
                        {
                            await SendMessageUnreliable(HEARTBEAT, _clientModeDestinationAddress);
                            _clientModeClientTimers.LastHeatbeatSentTime = DateTime.UtcNow;
                        }
                    }
                    await Task.Delay(RNMSDefaults.HeartbeatPollInterval, token);
                }
            }
            catch (TaskCanceledException) { }
        }

        public void SelfDisconnect()
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _udpClient.Close();
                InvokeOnSelfDisconnected();
                _clientModeClientTimers = null;
            }
        }

        public void Dispose()
        {
            SelfDisconnect();
            _cts.Dispose();
            _udpClient.Dispose();
            if (_isInListenerMode)
            {
                _listenerModeConnectedClients.Clear();
            }
        }
    }
}
