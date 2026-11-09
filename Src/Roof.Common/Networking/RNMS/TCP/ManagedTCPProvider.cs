using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.TCP
{
    public class ManagedTCPProvider : IDisposable
    {
        private class ClientState
        {
            public TcpClient TcpClient { get; set; }
            public NetworkStream Stream { get; set; }
            public CancellationTokenSource ReadLoopCts { get; set; }
            public DateTime? LastMessageReceivedTime { get; set; }
            public DateTime? LastHeartbeatSentTime { get; set; }
            public DateTime? LastHeartbeatReceivedTime { get; set; }
        }

        private const int FRAME_HEADER_SIZE = sizeof(int);
        private const int MAX_FRAME_SIZE_BYTES = 64 * 1024 * 1024;
        private static readonly TimeSpan DisconnectionPollInterval = TimeSpan.FromMilliseconds(150);
        private const string HEARTBEAT = "MTCP_HEARTBEAT";
        private const string DATA_PREFIX = "MTCP_DATA:";
        
        private static readonly byte[] DataPrefixBytes = Encoding.ASCII.GetBytes(DATA_PREFIX);
        private static readonly byte[] HeartbeatBytes = Encoding.ASCII.GetBytes(HEARTBEAT);

        private readonly bool _isInListenerMode;
        private bool _isInClientMode => !_isInListenerMode;
        
        private readonly object _sync = new object();
        private readonly Encoding _utf8 = new UTF8Encoding(false);
        
        private int _disposed; // 0 = active, 1 = disposed
        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        // Listener
        private TcpListener _listener;

        // Client mode
        private IPEndPoint _clientModeDestinationEndPoint;
        private ClientState _clientModeState;

        // Listener mode clients
        private readonly Dictionary<IPEndPoint, ClientState> _connectedClients = new Dictionary<IPEndPoint, ClientState>();

        // Background tasks
        private Task _acceptTask;
        private Task _monitorDisconnectionTask;
        private Task _keepAliveHeartbeatTask;

        private CancellationTokenSource _cts;

        public delegate void MessageReceivedEventDelegate(IPEndPoint clientAddress, string message);
        public event MessageReceivedEventDelegate MessageReceived;
        public event EventHandler<IPEndPoint> ClientConnected;
        public event EventHandler<IPEndPoint> ClientDisconnected;
        public event EventHandler SelfDisconnected;

        public int Port
        {
            get
            {
                if (_isInListenerMode) return ((IPEndPoint)_listener.LocalEndpoint).Port;
                if (_clientModeState?.TcpClient?.Client?.LocalEndPoint is IPEndPoint ep) return ep.Port;
                return 0;
            }
        }

        /// <summary>
        /// Constructor.
        /// Listener mode: binds and accepts on destinationPort.
        /// Client mode: connects to destinationAddress:destinationPort.
        /// </summary>
        public ManagedTCPProvider(string destinationAddress, int destinationPort, bool isInListenerMode)
        {
            _isInListenerMode = isInListenerMode;
            _cts = new CancellationTokenSource();

            if (_isInListenerMode)
            {
                _listener = new TcpListener(IPAddress.Any, destinationPort);
                _listener.Start();
                _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            }
            else
            {
                _clientModeDestinationEndPoint = new IPEndPoint(IPAddress.Parse(destinationAddress), destinationPort);
                _clientModeState = new ClientState
                {
                    TcpClient = new TcpClient(),
                    ReadLoopCts = new CancellationTokenSource()
                };
                _clientModeState.TcpClient.Connect(_clientModeDestinationEndPoint);
                _clientModeState.Stream = _clientModeState.TcpClient.GetStream();
                _ = Task.Run(() => ReadLoopAsync(_clientModeState, _clientModeDestinationEndPoint, _clientModeState.ReadLoopCts.Token));
            }

            _monitorDisconnectionTask = Task.Run(() => MonitorClientDisconnectionAsync(_cts.Token));
            _keepAliveHeartbeatTask = Task.Run(() => SendHeartbeatAsync(_cts.Token));
        }

        #region Event Invokers
        protected virtual void InvokeOnMessageReceived(IPEndPoint clientAddress, string message)
        {
            if (!IsDisposed)
                MessageReceived?.Invoke(clientAddress, message);
        }

        protected virtual void InvokeOnClientConnected(IPEndPoint clientAddress)
        {
            if (!IsDisposed)
                ClientConnected?.Invoke(this, clientAddress);
        }

        protected virtual void InvokeOnClientDisconnected(IPEndPoint clientAddress)
        {
            if (!IsDisposed)
                ClientDisconnected?.Invoke(this, clientAddress);
        }

        protected virtual void InvokeOnSelfDisconnected()
        {
            if (!IsDisposed)
                SelfDisconnected?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Send API (data + heartbeat)
        // Frame = [len(4, big-endian)] [payload bytes...], where payload is "MTCP_DATA:" + user payload
        private static byte[] BuildLengthPrefixBigEndian(int length)
        {
            // Network byte order (big-endian)
            return new byte[]
            {
                (byte)((length >> 24) & 0xFF),
                (byte)((length >> 16) & 0xFF),
                (byte)((length >> 8) & 0xFF),
                (byte)(length & 0xFF)
            };
        }

        private static async Task WriteFramedAsync(NetworkStream stream, byte[] content, CancellationToken ct = default)
        {
            byte[] len = BuildLengthPrefixBigEndian(content.Length);
            await stream.WriteAsync(len, 0, len.Length, ct);
            await stream.WriteAsync(content, 0, content.Length, ct);
            await stream.FlushAsync(ct);
        }

        private async Task<bool> SendFramedAsync(byte[] content, IPEndPoint? clientEndPoint = null)
        {
            try
            {
                if (_isInListenerMode)
                {
                    if (clientEndPoint == null) throw new InvalidOperationException("In listener mode, a client endpoint must be provided.");
                    ClientState st;
                    lock (_sync)
                    {
                        if (!_connectedClients.TryGetValue(clientEndPoint, out st))
                            return false;
                    }
                    if (!st.TcpClient.Connected) return false;
                    await WriteFramedAsync(st.Stream, content);
                }
                else
                {
                    var st = _clientModeState;
                    if (st?.TcpClient == null || !st.TcpClient.Connected) return false;
                    await WriteFramedAsync(st.Stream, content);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> SendMessage(string message, IPEndPoint? clientEndPoint = null)
        {
            byte[] payload = _utf8.GetBytes(message);
            byte[] framed = DataPrefixBytes.Concat(payload).ToArray();
            return SendFramedAsync(framed, clientEndPoint);
        }

        public Task<bool> SendMessage(byte[] messageBuffer, IPEndPoint? clientEndPoint = null)
        {
            byte[] framed = new byte[DataPrefixBytes.Length + messageBuffer.Length];
            Buffer.BlockCopy(DataPrefixBytes, 0, framed, 0, DataPrefixBytes.Length);
            Buffer.BlockCopy(messageBuffer, 0, framed, DataPrefixBytes.Length, messageBuffer.Length);
            return SendFramedAsync(framed, clientEndPoint);
        }

        // "Unreliable" doesn't apply to TCP; use the same framing for heartbeats.
        private Task<bool> SendMessageUnreliable(string message, IPEndPoint? clientEndPoint = null)
        {
            byte[] framed = _utf8.GetBytes(message);
            return SendFramedAsync(framed, clientEndPoint);
        }
        #endregion

        #region Accept & Read Loops
        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    var remote = (IPEndPoint)tcpClient.Client.RemoteEndPoint;

                    var state = new ClientState
                    {
                        TcpClient = tcpClient,
                        Stream = tcpClient.GetStream(),
                        ReadLoopCts = new CancellationTokenSource(),
                        LastMessageReceivedTime = DateTime.UtcNow
                    };

                    lock (_sync)
                    {
                        _connectedClients[remote] = state;
                    }
                    InvokeOnClientConnected(remote);

                    _ = Task.Run(() => ReadLoopAsync(state, remote, state.ReadLoopCts.Token));
                }
            }
            catch (ObjectDisposedException) { /* shutting down */ }
            catch (Exception ex)
            {
                Console.WriteLine($"AcceptLoopAsync error: {ex.Message}");
            }
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int n = await stream.ReadAsync(buffer, offset + readTotal, count - readTotal, token);
                if (n == 0) throw new IOException("Remote closed the connection.");
                readTotal += n;
            }
        }

        private static int ParseLengthBigEndian(byte[] len4)
        {
            return (len4[0] << 24) | (len4[1] << 16) | (len4[2] << 8) | len4[3];
        }

        private async Task ReadLoopAsync(ClientState state, IPEndPoint remoteEndPoint, CancellationToken token)
        {
            var stream = state.Stream;
            byte[] lenBuf = new byte[FRAME_HEADER_SIZE];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Read 4-byte length prefix
                    await ReadExactAsync(stream, lenBuf, 0, FRAME_HEADER_SIZE, token);
                    int contentLen = ParseLengthBigEndian(lenBuf);

                    if (contentLen < 0 || contentLen > MAX_FRAME_SIZE_BYTES) // sanity cap: 64MB
                        throw new IOException($"Invalid frame length: {contentLen}");

                    byte[] content = new byte[contentLen];
                    await ReadExactAsync(stream, content, 0, contentLen, token);

                    state.LastMessageReceivedTime = DateTime.UtcNow;

                    // Heartbeat frame is exactly "MTCP_HEARTBEAT"
                    if (contentLen == HeartbeatBytes.Length &&
                        content.AsSpan().SequenceEqual(HeartbeatBytes))
                    {
                        state.LastHeartbeatReceivedTime = DateTime.UtcNow;
                        continue;
                    }

                    // Data frame starts with "MTCP_DATA:"
                    if (contentLen >= DataPrefixBytes.Length &&
                        content.AsSpan(0, DataPrefixBytes.Length).SequenceEqual(DataPrefixBytes))
                    {
                        int payloadLen = contentLen - DataPrefixBytes.Length;
                        string payload = _utf8.GetString(content, DataPrefixBytes.Length, payloadLen);

                        if (_isInListenerMode)
                            InvokeOnMessageReceived(remoteEndPoint, payload);
                        else
                            InvokeOnMessageReceived(_clientModeDestinationEndPoint, payload);

                        continue;
                    }

                    // Unknown frame: ignore or log
                    Console.WriteLine($"Unknown MTCP frame from {remoteEndPoint} (len={contentLen}).");
                }
            }
            catch (IOException)
            {
                HandleDisconnect(remoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                HandleDisconnect(remoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReadLoop error from {remoteEndPoint}: {ex.Message}");
                HandleDisconnect(remoteEndPoint);
            }
        }

        private void HandleDisconnect(IPEndPoint remote)
        {
            if (IsDisposed)
                return;
            
            if (_isInListenerMode)
            {
                ClientState st;
                lock (_sync)
                {
                    if (!_connectedClients.TryGetValue(remote, out st))
                        return;

                    _connectedClients.Remove(remote);
                }

                try
                {
                    st.ReadLoopCts?.Cancel();
                    st.Stream?.Dispose();
                    st.TcpClient?.Close();
                    st.ReadLoopCts?.Dispose();
                }
                catch { }
                InvokeOnClientDisconnected(remote);
            }
            else
            {
                try
                {
                    _clientModeState?.ReadLoopCts?.Cancel();
                    _clientModeState?.Stream?.Dispose();
                    _clientModeState?.TcpClient?.Close();
                }
                catch { /* ignore */ }
                InvokeOnClientDisconnected(_clientModeDestinationEndPoint);
                SelfDisconnect();
            }
        }
        #endregion

        #region Heartbeats & Disconnection Monitor
        private async Task SendHeartbeatAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isInListenerMode)
                    {
                        List<KeyValuePair<IPEndPoint, ClientState>> snapshot;
                        lock (_sync)
                        {
                            snapshot = _connectedClients.ToList();
                        }
                        
                        foreach (var kvp in snapshot)
                        {
                            var ep = kvp.Key;
                            var st = kvp.Value;
                            if (st.LastHeartbeatSentTime == null ||
                                DateTime.UtcNow - st.LastHeartbeatSentTime > RNMSDefaults.HeartbeatInterval)
                            {
                                await SendMessageUnreliable(HEARTBEAT, ep);
                                st.LastHeartbeatSentTime = DateTime.UtcNow;
                            }
                        }
                    }
                    else
                    {
                        var st = _clientModeState;
                        if (st != null && (st.LastHeartbeatSentTime == null ||
                                           DateTime.UtcNow - st.LastHeartbeatSentTime > RNMSDefaults.HeartbeatInterval))
                        {
                            await SendMessageUnreliable(HEARTBEAT, null);
                            st.LastHeartbeatSentTime = DateTime.UtcNow;
                        }
                    }

                    await Task.Delay(RNMSDefaults.HeartbeatPollInterval, token);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task MonitorClientDisconnectionAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isInListenerMode)
                    {
                        List<KeyValuePair<IPEndPoint, ClientState>> snapshot;
                        lock (_sync)
                        {
                            snapshot = _connectedClients.ToList();
                        }
                        
                        foreach (var kvp in snapshot)
                        {
                            var ep = kvp.Key;
                            var st = kvp.Value;

                            DateTime? lastActivity = st.LastHeartbeatReceivedTime ?? st.LastMessageReceivedTime;
                            if (lastActivity != null && DateTime.UtcNow - lastActivity > RNMSDefaults.DisconnectionTimeout)
                            {
                                lock (_sync)
                                {
                                    _connectedClients.Remove(ep);
                                }

                                try
                                {
                                    st.ReadLoopCts?.Cancel();
                                    st.Stream?.Dispose();
                                    st.TcpClient?.Close();
                                    st.ReadLoopCts?.Dispose();
                                }
                                catch { }

                                InvokeOnClientDisconnected(ep);
                            }
                        }
                    }
                    else
                    {
                        var st = _clientModeState;
                        if (st != null)
                        {
                            DateTime? lastReceived = st.LastHeartbeatReceivedTime ?? st.LastMessageReceivedTime;
                            if (lastReceived != null && DateTime.UtcNow - lastReceived > RNMSDefaults.DisconnectionTimeout)
                            {
                                InvokeOnClientDisconnected(_clientModeDestinationEndPoint);
                                SelfDisconnect();
                                break;
                            }
                        }
                    }

                    await Task.Delay(DisconnectionPollInterval, token);
                }
            }
            catch (TaskCanceledException) { }
        }
        #endregion

        #region Lifecycle
        public void SelfDisconnect()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try { _cts?.Cancel(); } catch { }

            if (_isInListenerMode)
            {
                try { _listener?.Stop(); } catch { }

                List<KeyValuePair<IPEndPoint, ClientState>> clients;

                lock (_sync)
                {
                    clients = _connectedClients.ToList();
                    _connectedClients.Clear();
                }

                foreach (var kvp in clients)
                {
                    try { kvp.Value.ReadLoopCts?.Cancel(); } catch { }
                    try { kvp.Value.Stream?.Dispose(); } catch { }
                    try { kvp.Value.TcpClient?.Close(); } catch { }
                    try { kvp.Value.ReadLoopCts?.Dispose(); } catch { }
                }
            }
            else
            {
                var st = _clientModeState;
                _clientModeState = null;

                if (st != null)
                {
                    try { st.ReadLoopCts?.Cancel(); } catch { }
                    try { st.Stream?.Dispose(); } catch { }
                    try { st.TcpClient?.Close(); } catch { }
                    try { st.ReadLoopCts?.Dispose(); } catch { }
                }
            }

            // Do not invoke SelfDisconnected here because callbacks after disposal are blocked.
        }

        public void Dispose()
        {
            SelfDisconnect();
            try { _cts?.Dispose(); } catch { }
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}