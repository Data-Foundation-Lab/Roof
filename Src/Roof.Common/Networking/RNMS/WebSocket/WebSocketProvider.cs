using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.WebSocket
{
    public class WebSocketProvider : IDisposable
    {
        private const int RECEIVE_BUFFER_SIZE = 1024;
        private static readonly TimeSpan HeartbeatPollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan DisconnectionPollInterval = TimeSpan.FromSeconds(1);

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private ClientWebSocket _client;
        private string _clientID;

        private DateTime? _lastHeartbeatSentTime;
        private DateTime? _lastHeartbeatReceivedTime;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _listenerTask;
        private Task _heartbeatTask;
        private Task _monitorDisconnectionTask;
        private volatile bool _isDisposed;
        private int _clientDisconnectedRaised;

        public delegate void WebSocketMessageReceivedEventDelegate(RoofWebSocketPacket message);
        public event WebSocketMessageReceivedEventDelegate MessageReceived;
        public event EventHandler<string> ClientConnected;
        public event EventHandler<string> ClientDisconnected;

        /// <summary>
        /// Indicates whether the WebSocket connection is open.
        /// </summary>
        public bool IsConnected => !_isDisposed && Volatile.Read(ref _clientDisconnectedRaised) == 0 && _client?.State == WebSocketState.Open;

        /// <summary>
        /// Initializes a new instance of the WebSocketClient class.
        /// </summary>
        public WebSocketProvider()
        {
            _client = new ClientWebSocket();
        }

        public void SetClientID(string clientID) => _clientID = clientID;

        public async Task<bool> Initialize(Uri uri, CancellationToken cancellationToken = default)
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
                await _client.ConnectAsync(uri, linkedCts.Token);
            }
            catch (Exception ex)
            {
                //Dont continue the control flow.
                //Console.WriteLine(ex.ToString());
                return false;
            }

            _lastHeartbeatReceivedTime = DateTime.UtcNow;
            ClientConnected?.Invoke(this, "");

            _listenerTask = Task.Run(() => ListenToSocketMessages(_cts.Token));
            _heartbeatTask = Task.Run(() => SendHeartbeat(_cts.Token));
            _monitorDisconnectionTask = Task.Run(() => MonitorDisconnection(_cts.Token));

            return true;
        }

        /// <summary>
        /// Sends a text message over the WebSocket connection.
        /// </summary>
        /// <param name="message">The text message to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<bool> SendAsync(string message, CancellationToken cancellationToken = default)
        {
            return await SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, cancellationToken);
        }

        /// <summary>
        /// Sends binary data over the WebSocket connection.
        /// </summary>
        /// <param name="data">The binary data to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            return SendAsync(data, WebSocketMessageType.Binary, cancellationToken);
        }

        private async Task<bool> SendAsync(byte[] data, WebSocketMessageType messageType, CancellationToken cancellationToken)
        {
            bool lockTaken = false;
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
                await _sendLock.WaitAsync(linkedCts.Token);
                lockTaken = true;
                if (!IsConnected)
                {
                    InvokeClientDisconnected();
                    return false;
                }
                await _client.SendAsync(new ArraySegment<byte>(data), messageType, true, linkedCts.Token);
                return true;
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine(ex.ToString());
                InvokeClientDisconnected();
                return false;
            }
            catch (OperationCanceledException ioEx)
            {
                Console.WriteLine($"{ioEx.ToString()}");
                if (!IsConnected)
                    InvokeClientDisconnected();
                return false;
            }
            catch (ObjectDisposedException)
            {
                InvokeClientDisconnected();
                return false;
            }
            finally
            {
                if (lockTaken)
                    _sendLock.Release();
            }
        }

        /*
        /// <summary>
        /// Receives a complete message from the WebSocket connection.
        /// If the message is text, returns the decoded string.
        /// For binary messages, returns the Base64-encoded string representation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The received message as a string, or null if the connection was closed.</returns>
        public async Task<string> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return "";

            // A buffer for receiving data (adjust size as needed)
            var buffer = new ArraySegment<byte>(new byte[RECEIVE_BUFFER_SIZE]);

            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _client.ReceiveAsync(buffer, cancellationToken);

                    // If the server initiated a close, then close our side too.
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseAsync(cancellationToken);
                        return null;
                    }

                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    using (var reader = new StreamReader(ms, Encoding.UTF8))
                    {
                        return await reader.ReadToEndAsync();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // For binary messages, return a Base64 encoded string.
                    return Convert.ToBase64String(ms.ToArray());
                }
                else
                {
                    throw new NotSupportedException("Unsupported message type.");
                }
            }
        }*/

        /// <summary>
        /// Closes the WebSocket connection gracefully.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await CloseAsync(WebSocketCloseStatus.NormalClosure, WebSocketProtocolConstants.NORMAL_CLOSE_REASON, cancellationToken);
            _client?.Dispose();
        }

        private async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            if (_client != null && (IsConnected || _client.State == WebSocketState.CloseReceived))
            {
                try
                {
                    // Attempt the original close handshake, but do not let a missing peer stall cleanup.
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    closeCts.CancelAfter(TimeSpan.FromSeconds(2));
                    await _client.CloseAsync(status, description, closeCts.Token);
                }
                catch
                {
                    // Ignore any errors during close.
                }
            }
            InvokeClientDisconnected();
        }

        private async Task ListenToSocketMessages(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_client.State != WebSocketState.Open)
                    {
                        InvokeClientDisconnected();
                        break;
                    }
                    
                    byte[] buffer = new byte[RECEIVE_BUFFER_SIZE];

                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                            // If the peer initiated a close, close our side.
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await CloseAsync(WebSocketCloseStatus.NormalClosure, WebSocketProtocolConstants.NORMAL_CLOSE_REASON, token);
                                return;
                            }

                            // Write the received chunk into the memory stream.
                            ms.Write(buffer, 0, result.Count);

                        } while (!result.EndOfMessage);

                        // Convert the accumulated bytes into a complete message.
                        byte[] completeMessage = ms.ToArray();

                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            RoofWebSocketPacket? webSocketPacket = null;
                            try
                            {
                                // Attempt to deserialize the complete message.
                                webSocketPacket = JsonSerializer.Deserialize(
                                    completeMessage,
                                    RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket
                                );
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            if (webSocketPacket != null)
                            {
                                //Detect heartbeat message
                                if (webSocketPacket.Payload == WebSocketProtocolConstants.HEARTBEAT &&
                                    webSocketPacket.SenderIdentifier == NetworkCommonIdentifiers.PATHFINDER_GLOBAL_IDENTIFIER &&
                                    webSocketPacket.ForwardingToIdentifier == _clientID)
                                {
                                    //Console.WriteLine($"Received WS_HEARTBEAT at {DateTime.UtcNow}");
                                    _lastHeartbeatReceivedTime = DateTime.UtcNow;

                                    continue;
                                }

                                //If the message conforms to our WebSocket packet structure, allow it.
                                try
                                {
                                    MessageReceived?.Invoke(webSocketPacket);
                                }
                                catch (Exception)
                                {
                                    // A bad packet or handler must not stop the receive loop.
                                }
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (WebSocketException) { }
            finally
            {
                InvokeClientDisconnected();
            }
        }

        private async Task MonitorDisconnection(CancellationToken cts)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    // Check if client state indicates disconnection
                    if (!IsConnected)
                    {
                        InvokeClientDisconnected();
                        break;
                    }

                    // Check the heartbeat deadline, including when no first heartbeat arrives.
                    if (_lastHeartbeatReceivedTime != null && DateTime.UtcNow - _lastHeartbeatReceivedTime > RNMSDefaults.DisconnectionTimeout)
                    {
                        if (IsConnected)
                        {
                            await CloseAsync(WebSocketCloseStatus.EndpointUnavailable, WebSocketProtocolConstants.HEARTBEAT_TIMEOUT_CLOSE_REASON, cts);
                            break;
                        }
                    }

                    await Task.Delay(DisconnectionPollInterval, cts);
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task SendHeartbeat(CancellationToken cts)
        {
            try
            {
                while(!cts.IsCancellationRequested)
                {
                    if(_lastHeartbeatSentTime == null || DateTime.UtcNow - _lastHeartbeatSentTime > RNMSDefaults.HeartbeatInterval)
                    {
                        if (IsConnected)
                        {
                            var heartbeatPacket = new RoofWebSocketPacket()
                            {
                                SenderIdentifier = _clientID,
                                ForwardingToIdentifier = NetworkCommonIdentifiers.PATHFINDER_GLOBAL_IDENTIFIER,
                                Payload = WebSocketProtocolConstants.HEARTBEAT
                            };
                            var serializedHeartbeat = JsonSerializer.Serialize(heartbeatPacket, RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket);

                            byte[] heartbeatPacketBuffer = Encoding.UTF8.GetBytes(serializedHeartbeat);

                            await SendAsync(heartbeatPacketBuffer, cts);

                            _lastHeartbeatSentTime = DateTime.UtcNow;
                        }
                    }

                    await Task.Delay(HeartbeatPollInterval, cts);
                }
            }
            catch(TaskCanceledException) { }
            catch(OperationCanceledException) { }
        }

        private void InvokeClientDisconnected()
        {
            if (_isDisposed || Interlocked.Exchange(ref _clientDisconnectedRaised, 1) == 1)
                return;

            try { _cts.Cancel(); } catch { }
            try { _client.Abort(); } catch { }
            ClientDisconnected?.Invoke(this, "");
        }

        /// <summary>
        /// Disposes the underlying ClientWebSocket.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try { _cts.Cancel(); } catch { }
            try { _client?.Abort(); } catch { }
            try { _client?.Dispose(); } catch { }

            try
            {
                Task.WaitAll(
                    new[] { _listenerTask, _heartbeatTask, _monitorDisconnectionTask }
                        .Where(task => task != null)
                        .ToArray(),
                    TimeSpan.FromSeconds(2));
            }
            catch { }

            _cts.Dispose();
        }
    }
}
