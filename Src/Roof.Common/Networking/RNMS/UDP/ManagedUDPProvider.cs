using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.UDP
{
    public class ManagedUDPProvider : IDisposable
    {
        private const string HEARTBEAT = "MUDP_HEARTBEAT";
        private const string DATA_PREFIX = "MUDP_DATA:";
        private const string ACK_PREFIX = "MUDP_ACK:";

        /// <summary>
        /// Class to track a reliable message waiting for an ACK.
        /// </summary>
        public class PendingMessage
        {
            public int SequenceNumber { get; set; }
            public byte[] MessageBytes { get; set; }
            public int RetryCount { get; set; }
            public DateTime? LastSent { get; set; }
            public IPEndPoint ClientEndPoint { get; set; }
        }

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

        #region RELIABILITY_FIELDS
        // ******************** Reliability Fields ********************
        // Sequence number generator for reliable messages.
        private int _currentSequenceNumber = 0;
        // Store pending messages by their sequence number.
        private readonly ConcurrentDictionary<int, PendingMessage> _pendingMessages = new ConcurrentDictionary<int, PendingMessage>();
        // Retransmission interval and maximum retry count.
        private static readonly TimeSpan RetransmissionInterval = TimeSpan.FromSeconds(2);
        private const int MAX_RETRIES = 5;
        // *************************************************************
        #endregion

        private Task _listeningTask;
        private Task _monitorDisconnectionTask;
        private Task _retransmitTask;
        private Task _keepAliveHeartbeatTask;

        public delegate void MessageReceivedEventDelegate(IPEndPoint clientAddress, string message);
        public event MessageReceivedEventDelegate MessageReceived;
        public event EventHandler<IPEndPoint> ClientConnected;
        public event EventHandler<IPEndPoint> ClientDisconnected;
        public event EventHandler SelfDisconnected;

        public event EventHandler<int> MessageFailed; // Fired when a reliable message fails after maximum retries.
        public event EventHandler<int> MessageAcknowledged; // Fired when a reliable message is acknowledged.

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
        public ManagedUDPProvider(string destinationAddress, int destinationPort, bool isInListenerMode)
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
            _retransmitTask = Task.Run(() => RetransmitPendingMessagesAsync(_cts.Token));
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

        protected virtual void InvokeOnMessageFailed(int sequenceNumber) =>
            MessageFailed?.Invoke(this, sequenceNumber);

        protected virtual void InvokeOnMessageAcknowledged(int sequenceNumber) =>
            MessageAcknowledged?.Invoke(this, sequenceNumber);
        #endregion

        #region Reliable Message Building & Sending

        /// <summary>
        /// Builds a reliable message. The message is formatted as:
        /// "MUDP_DATA:{sequence}:{payload}"
        /// and stored for potential retransmission.
        /// </summary>
        /// <param name="message">The payload string.</param>
        /// <param name="clientAddress">
        /// For client mode this is not needed (the target is already set).
        /// For listener mode you must provide the target client's endpoint.
        /// </param>
        /// <returns>The formatted message string.</returns>
        public string BuildMessage(string message, IPEndPoint? clientAddress = null)
        {
            CreatePendingMessage(message, clientAddress, out string formattedMessage);
            return formattedMessage;
        }

        private PendingMessage CreatePendingMessage(string message, IPEndPoint? clientAddress, out string formattedMessage)
        {
            // In listener mode, a client endpoint must be provided.
            if (_isInListenerMode && clientAddress == null)
            {
                throw new InvalidOperationException("In listener mode, a client address must be provided for reliable messages.");
            }

            int sequence = Interlocked.Increment(ref _currentSequenceNumber);
            formattedMessage = $"{DATA_PREFIX}{sequence}:{message}";
            byte[] formattedMessageBuffer = Encoding.UTF8.GetBytes(formattedMessage);
            
            var pending = new PendingMessage
            {
                SequenceNumber = sequence,
                MessageBytes = formattedMessageBuffer,
                LastSent = DateTime.UtcNow,
                RetryCount = 0,
                ClientEndPoint = clientAddress ?? _clientModeDestinationAddress
            };

            _pendingMessages[sequence] = pending;
            return pending;
        }

        /// <summary>
        /// Overload for building a reliable message from a byte array payload.
        /// </summary>
        public string BuildMessage(byte[] messageBuffer, IPEndPoint? clientAddress = null)
        {
            return BuildMessage(Encoding.UTF8.GetString(messageBuffer), clientAddress);
        }

        /// <summary>
        /// Sends a reliable message. This method builds the reliable packet,
        /// sends the exact bytes stored in the pending message, and then the
        /// retransmission mechanism (if needed) will handle resends.
        /// </summary>
        /// <param name="message">The payload string.</param>
        /// <param name="clientAddress">
        /// For client mode this is optional.
        /// For listener mode you must provide the target client's endpoint.
        /// </param>
        public async Task<bool> SendMessage(string message, IPEndPoint? clientAddress = null)
        {
            PendingMessage pendingMessage = CreatePendingMessage(message, clientAddress, out _);
            await _udpClient.SendAsync(pendingMessage.MessageBytes, pendingMessage.MessageBytes.Length, pendingMessage.ClientEndPoint);
            return true;
        }

        /// <summary>
        /// Sends a reliable message. This method builds the reliable packet,
        /// sends the exact bytes stored in the pending message, and then the
        /// retransmission mechanism (if needed) will handle resends.
        /// </summary>
        /// <param name="messageBuffer">The UTF-8 encoded payload.</param>
        /// <param name="clientAddress">
        /// For client mode this is optional.
        /// For listener mode you must provide the target client's endpoint.
        /// </param>
        public async Task<bool> SendMessage(byte[] messageBuffer, IPEndPoint? clientAddress = null)
        {
            return await SendMessage(Encoding.UTF8.GetString(messageBuffer), clientAddress);
        }
        #endregion

        #region Non-Reliable Sending (e.g. heartbeats)

        /// <summary>
        /// Sends a message without reliability tracking. Useful for heartbeats or other non-critical data.
        /// </summary>
        private async Task<bool> SendMessageUnreliable(string message, IPEndPoint? clientAddress = null)
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
        private async Task<bool> SendMessageUnreliable(byte[] messageBuffer, IPEndPoint? clientAddress = null)
        {
            IPEndPoint targetEndpoint = clientAddress ?? (_isInClientMode ? _clientModeDestinationAddress : throw new InvalidOperationException(RNMSErrorMessages.CLIENT_ADDRESS_REQUIRED_ERROR));
            await _udpClient.SendAsync(messageBuffer, messageBuffer.Length, targetEndpoint);
            return true;
        }
        #endregion

        private async Task ListenForMessagesAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                IPEndPoint remoteEndPoint = null;
                try
                {
                    UdpReceiveResult udpResult = await _udpClient.ReceiveAsync();
                    remoteEndPoint = udpResult.RemoteEndPoint;

                    // In client mode, if the sender isn't our target, disconnect.
                    if (_isInClientMode && !remoteEndPoint.Equals(_clientModeDestinationAddress))
                    {
                        SelfDisconnect();
                        continue;
                    }

                    // In listener mode, add new clients as they send messages.
                    if (_isInListenerMode && !_listenerModeConnectedClients.ContainsKey(remoteEndPoint))
                    {
                        _listenerModeConnectedClients.Add(remoteEndPoint, new ClientTimers { LastMessageReceivedTime = DateTime.UtcNow });
                        InvokeOnClientConnected(remoteEndPoint);
                    }

                    string message = Encoding.UTF8.GetString(udpResult.Buffer);

                    // Update last received time.
                    if (_isInClientMode)
                    {
                        _clientModeClientTimers.LastMessageReceivedTime = DateTime.UtcNow;
                    }
                    else if (_isInListenerMode && _listenerModeConnectedClients.ContainsKey(remoteEndPoint))
                    {
                        _listenerModeConnectedClients[remoteEndPoint].LastMessageReceivedTime = DateTime.UtcNow;
                    }

                    // Handle heartbeat messages.
                    if (message.Equals(HEARTBEAT))
                    {
                        if (_isInClientMode)
                        {
                            _clientModeClientTimers.LastHeatbeatReceivedTime = DateTime.UtcNow;
                        }
                        else if (_isInListenerMode && _listenerModeConnectedClients.TryGetValue(remoteEndPoint, out ClientTimers clientTimers))
                        {
                            clientTimers.LastHeatbeatReceivedTime = DateTime.UtcNow;
                        }
                        continue; // Skip further processing.
                    }

                    // Process ACKs. Expected format: "MUDP_ACK:{sequence}"
                    if (message.StartsWith(ACK_PREFIX, StringComparison.Ordinal))
                    {
                        var parts = message.Split(':');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int ackSeq))
                        {
                            if (_pendingMessages.TryRemove(ackSeq, out _))
                            {
                                InvokeOnMessageAcknowledged(ackSeq);
                            }
                        }
                        continue;
                    }

                    // Process data messages. Expected format: "MUDP_DATA:{seq}:{payload}"
                    if (message.StartsWith(DATA_PREFIX, StringComparison.Ordinal))
                    {
                        int sequenceIndex = message.IndexOf(':');
                        int messageIndex = message.IndexOf(':', sequenceIndex + 1);
                        if (sequenceIndex != -1 && messageIndex != -1)
                        {
                            string seqStr = message.Substring(sequenceIndex + 1, messageIndex - sequenceIndex - 1);
                            if (int.TryParse(seqStr, out int seq))
                            {
                                // Extract payload.
                                string payload = message.Substring(messageIndex + 1);

                                // Immediately send an ACK back.
                                string ackMessage = $"{ACK_PREFIX}{seq}";
                                byte[] ackBytes = Encoding.ASCII.GetBytes(ackMessage);
                                if (_isInListenerMode)
                                {
                                    await _udpClient.SendAsync(ackBytes, ackBytes.Length, remoteEndPoint);
                                    InvokeOnMessageReceived(remoteEndPoint, payload);
                                }
                                else
                                {
                                    await _udpClient.SendAsync(ackBytes, ackBytes.Length, _clientModeDestinationAddress);
                                    InvokeOnMessageReceived(_clientModeDestinationAddress, payload);
                                }
                            }
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Likely a shutdown so break out of the loop.
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message from {remoteEndPoint}: {ex.Message}");

                    // If in client mode, nullify the client mode destination address.
                    if (_isInClientMode)
                    {
                        _clientModeDestinationAddress = null;
                    }
                    // If in listener mode, remove the problematic IPEndPoint to allow it to reconnect later.
                    else if (_isInListenerMode && remoteEndPoint != null)
                    {
                        _listenerModeConnectedClients.Remove(remoteEndPoint);
                    }
                }
            }
        }

        // Periodically checks pending messages and retransmits those that haven't been acknowledged.
        private async Task RetransmitPendingMessagesAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var kvp in _pendingMessages.ToList())
                    {
                        var pendingMessage = kvp.Value;
                        if (pendingMessage != null && (pendingMessage.LastSent == null || DateTime.UtcNow - pendingMessage.LastSent > RetransmissionInterval))
                        {
                            if (pendingMessage.RetryCount >= MAX_RETRIES)
                            {
                                if (_pendingMessages.TryRemove(pendingMessage.SequenceNumber, out _))
                                {
                                    InvokeOnMessageFailed(pendingMessage.SequenceNumber);
                                }
                            }
                            else
                            {
                                await _udpClient.SendAsync(pendingMessage.MessageBytes, pendingMessage.MessageBytes.Length, pendingMessage.ClientEndPoint);
                                pendingMessage.RetryCount++;
                                pendingMessage.LastSent = DateTime.UtcNow;
                            }
                        }
                    }
                    await Task.Delay(RNMSDefaults.UdpRetransmissionPollInterval, token);
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RetransmitPendingMessagesAsync: {ex.Message}");
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
