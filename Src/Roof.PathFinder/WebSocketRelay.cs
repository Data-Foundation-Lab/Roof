using Roof.Common.Networking.RNMS.WebSocket;
using Roof.Common.Tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Roof.PathFinder
{
    public class PathFinderClient
    {
        public string? Identifier { get; set; }                 // Set after first good packet
        public string ConnectionId { get; set; } = "";          // Derived from Sec-WebSocket-Key per handshake
        public WebSocket? Socket { get; set; }
        public string? OriginIP { get; set; }                   // From CF-Connecting-IP
        public string? RayId { get; set; }                      // From CF-Ray (debug)
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastHeartbeatSentTime { get; set; }
        public DateTime? LastHeartbeatReceivedTime { get; set; }
    }

    internal class WebSocketRelay
    {
        private const int WEBSOCKET_TCP_LISTENER_PORT = 8071;

        // Handshake and relay-only wire values stay local to PathFinder.
        private const string HTTP_LINE_ENDING = "\r\n";
        private const string WEBSOCKET_UPGRADE_HEADER = "Upgrade: websocket";
        private const string CONNECTION_UPGRADE_HEADER = "Connection: Upgrade";
        private const string WEBSOCKET_KEY_HEADER = "Sec-WebSocket-Key";
        private const string WEBSOCKET_ACCEPT_HEADER = "Sec-WebSocket-Accept";
        private const string WEBSOCKET_ACCEPT_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const string SWITCHING_PROTOCOLS_STATUS = "HTTP/1.1 101 Switching Protocols";
        private const string CONNECTING_IP_HEADER = "CF-Connecting-IP";
        private const string RAY_ID_HEADER = "CF-Ray";
        private const string IDENTIFYING_TIMEOUT_CLOSE_REASON = "Identifying packet timeout.";
        private const string SHUTDOWN_CLOSE_REASON = "PathFinder shutting down.";

        // Authoritative registry AFTER the client announces SenderIdentifier
        private static readonly ConcurrentDictionary<string, PathFinderClient> _clientsByIdentifier = new(StringComparer.Ordinal);

        // Temporary registry keyed by per-handshake connectionId (Sec-WebSocket-Key)
        private static readonly ConcurrentDictionary<string, PathFinderClient> _connectionsById = new(StringComparer.Ordinal);

        private static readonly TcpListener _listener = new(IPAddress.Any, WEBSOCKET_TCP_LISTENER_PORT);

        private static readonly TimeSpan _disconnectionTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan _defaultHeatbeatRecurrenceTime = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _unidentifiedConnectionTimeout = _defaultHeatbeatRecurrenceTime;
        private static readonly TimeSpan _shutdownGraceTimeout = TimeSpan.FromSeconds(5);

        private static Task? _heartBeatTask;
        private static Task? _detectDisconnectionTask;
        private static CancellationTokenSource? _taskCTS;
        private static int _shutdownStarted;

        public static async Task Start()
        {
            _listener.Start();

            _taskCTS = new CancellationTokenSource();

            _heartBeatTask = Task.Run(() => SendHeartbeatForEachClient(_taskCTS.Token));
            _detectDisconnectionTask = Task.Run(() => DetectDisconnection(_taskCTS.Token));

            Logger.Log($"Started PathFinder on port {WEBSOCKET_TCP_LISTENER_PORT}. Awaiting connections...");

            try
            {
                while (!_taskCTS.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClient(client);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        public static async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
                return;

            _taskCTS?.Cancel();

            try { _listener.Stop(); } catch { }

            var identifiedClients = _clientsByIdentifier.ToArray();
            var temporaryConnections = _connectionsById
                .ToArray()
                .Where(kvp => string.IsNullOrEmpty(kvp.Value.Identifier))
                .ToArray();

            Logger.Log(
                $"PathFinder shutdown closing sockets. identified={identifiedClients.Length} temporary={temporaryConnections.Length} timeout={_shutdownGraceTimeout.TotalSeconds:0}s");

            var shutdownResults = new ShutdownCloseResults();
            var closedConnectionIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            var closeTasks = new List<Task>();
            foreach (var kvp in identifiedClients)
            {
                closeTasks.Add(CloseClientForShutdown(
                    client: kvp.Value,
                    registryName: "identified",
                    registryKey: kvp.Key,
                    results: shutdownResults,
                    closedConnectionIds: closedConnectionIds));
            }

            foreach (var kvp in temporaryConnections)
            {
                closeTasks.Add(CloseClientForShutdown(
                    client: kvp.Value,
                    registryName: "temporary",
                    registryKey: kvp.Key,
                    results: shutdownResults,
                    closedConnectionIds: closedConnectionIds));
            }

            await Task.WhenAll(closeTasks);

            _clientsByIdentifier.Clear();
            _connectionsById.Clear();

            Logger.Log(
                $"PathFinder shutdown close results. attempted={shutdownResults.Attempted} closed={shutdownResults.Closed} alreadyClosed={shutdownResults.AlreadyClosed} aborted={shutdownResults.Aborted} failed={shutdownResults.Failed}");

            _listener.Dispose();
        }

        public static void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private static async Task HandleClient(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();

                // Read the HTTP handshake request
                byte[] buffer = new byte[8192];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead <= 0) { client.Close(); return; }

                string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Quick checks
                if (!request.Contains(WEBSOCKET_UPGRADE_HEADER, StringComparison.OrdinalIgnoreCase) ||
                    !request.Contains(CONNECTION_UPGRADE_HEADER, StringComparison.OrdinalIgnoreCase))
                {
                    client.Close();
                    return;
                }

                // Parse headers
                var headers = ParseHeaders(request);
                headers.TryGetValue(CONNECTING_IP_HEADER, out var ip);
                headers.TryGetValue(RAY_ID_HEADER, out var ray);
                headers.TryGetValue(WEBSOCKET_KEY_HEADER, out var secWsKey);

                // Connection-level id (unique per handshake)
                var connectionId = !string.IsNullOrWhiteSpace(secWsKey) ? secWsKey : Guid.NewGuid().ToString("N");

                var handshakeResponse = BuildWebSocketHandshake(request);
                if (string.IsNullOrEmpty(handshakeResponse))
                {
                    Logger.Log("WebSocket handshake failed.", LogSeverity.Warning);
                    client.Close();
                    return;
                }

                await stream.WriteAsync(Encoding.UTF8.GetBytes(handshakeResponse));
                Logger.Log($"WebSocket handshake completed. conn={connectionId} ip={(ip ?? "-")} ray={(ray ?? "-")}");

                var webSocket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);

                // Register a temporary connection entry (unidentified until first good packet)
                var conn = new PathFinderClient
                {
                    Identifier = null, // will be set when SenderIdentifier arrives
                    ConnectionId = connectionId,
                    Socket = webSocket,
                    OriginIP = ip,
                    RayId = ray,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _connectionsById[connectionId] = conn;

                _ = ForwardAllPackets(webSocket, connectionId);
            }
            catch (Exception ex)
            {
                Logger.Log($"HandleClient(): Exception while handling client: {ex.Message}", LogSeverity.Danger);
            }
        }

        private static string BuildWebSocketHandshake(string request)
        {
            string key = "";
            foreach (var line in request.Split(new[] { HTTP_LINE_ENDING }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(WEBSOCKET_KEY_HEADER + ":", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring(line.IndexOf(":") + 1).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(key))
            {
                Logger.Log("Invalid WebSocket request: Missing Sec-WebSocket-Key", LogSeverity.Warning);
                return string.Empty;
            }

            string acceptKey = Convert.ToBase64String(
                System.Security.Cryptography.SHA1.Create().ComputeHash(
                    Encoding.UTF8.GetBytes(key + WEBSOCKET_ACCEPT_GUID))
            );

            return SWITCHING_PROTOCOLS_STATUS + HTTP_LINE_ENDING +
                   WEBSOCKET_UPGRADE_HEADER + HTTP_LINE_ENDING +
                   CONNECTION_UPGRADE_HEADER + HTTP_LINE_ENDING +
                   $"{WEBSOCKET_ACCEPT_HEADER}: {acceptKey}{HTTP_LINE_ENDING}{HTTP_LINE_ENDING}";
        }

        private static async Task SendHeartbeatForEachClient(CancellationToken cts)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    foreach (var kvp in _clientsByIdentifier.ToArray())
                    {
                        var client = kvp.Value;

                        if (client.LastHeartbeatSentTime == null ||
                            DateTime.UtcNow - client.LastHeartbeatSentTime > _defaultHeatbeatRecurrenceTime)
                        {
                            var heartbeatPacket = new RoofWebSocketPacket()
                            {
                                SenderIdentifier = Program.PATHFINDER_IDENTIFIER,
                                ForwardingToIdentifier = client.Identifier,
                                Payload = WebSocketProtocolConstants.HEARTBEAT
                            };
                            var serializedHeartbeat = JsonSerializer.Serialize(
                                heartbeatPacket, RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket);

                            byte[] heartbeatPacketBuffer = Encoding.UTF8.GetBytes(serializedHeartbeat);

                            if (client.Socket?.State == WebSocketState.Open)
                            {
                                await client.Socket.SendAsync(
                                    new ArraySegment<byte>(heartbeatPacketBuffer),
                                    WebSocketMessageType.Binary, true, cts);
                                client.LastHeartbeatSentTime = DateTime.UtcNow;
                            }
                            else
                            {
                                // Stale socket—remove from identified map
                                _clientsByIdentifier.TryRemove(kvp);
                            }
                        }
                    }

                    await Task.Delay(250, cts);
                }
            }
            catch (TaskCanceledException) { }
        }

        private static async Task DetectDisconnection(CancellationToken cts)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    // 1) Identified clients
                    foreach (var kvp in _clientsByIdentifier.ToArray())
                    {
                        var client = kvp.Value;
                        var id = client.Identifier ?? "(null)";

                        if (client.Socket == null ||
                            client.Socket.State == WebSocketState.Closed ||
                            client.Socket.State == WebSocketState.Aborted)
                        {
                            Logger.Log($"Detected disconnection of client [{id}]");
                            _clientsByIdentifier.TryRemove(kvp);
                            continue;
                        }

                        DateTime lastHeartbeat = client.LastHeartbeatReceivedTime ?? client.LastHeartbeatSentTime ?? DateTime.UtcNow;
                        if (DateTime.UtcNow - lastHeartbeat > _disconnectionTimeout)
                        {
                            Logger.Log($"Detected disconnection of [{id}] due to heartbeat timeout.");
                            try { await client.Socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, WebSocketProtocolConstants.HEARTBEAT_TIMEOUT_CLOSE_REASON, cts); } catch { }
                            _clientsByIdentifier.TryRemove(kvp);
                            continue;
                        }
                    }

                    // 2) Temporary connections (not yet identified)
                    foreach (var kvp in _connectionsById.ToArray())
                    {
                        var temp = kvp.Value;
                        if (temp.Socket == null ||
                            temp.Socket.State == WebSocketState.Closed ||
                            temp.Socket.State == WebSocketState.Aborted)
                        {
                            _connectionsById.TryRemove(kvp);
                            continue;
                        }

                        if (DateTime.UtcNow - temp.CreatedAtUtc > _unidentifiedConnectionTimeout)
                        {
                            Logger.Log(
                                $"Unidentified connection timeout. conn={temp.ConnectionId} ip={(temp.OriginIP ?? "-")} ray={(temp.RayId ?? "-")} timeout={_unidentifiedConnectionTimeout.TotalSeconds:0}s",
                                LogSeverity.Warning);

                            try
                            {
                                await temp.Socket.CloseAsync(
                                    WebSocketCloseStatus.PolicyViolation,
                                    IDENTIFYING_TIMEOUT_CLOSE_REASON,
                                    cts);
                            }
                            catch { /* ignore */ }

                            _connectionsById.TryRemove(kvp);
                        }
                    }

                    await Task.Delay(1000, cts);
                }
            }
            catch (TaskCanceledException) { }
        }

        private static async Task ForwardAllPackets(WebSocket socket, string connectionId)
        {
            try
            {
                byte[] buffer = new byte[4096];
                using var message = new MemoryStream();

                while (socket.State == WebSocketState.Open)
                {
                    var messageType = await ReceiveMessage(socket, buffer, message);
                    if (messageType == WebSocketMessageType.Close)
                    {
                        Logger.Log($"A client disconnected (conn={connectionId}).");
                        await SafeCloseAndCleanup(socket, connectionId);
                        return;
                    }

                    if (messageType == WebSocketMessageType.Binary)
                    {
                        await ProcessPacket(
                            socket, connectionId,
                            message.GetBuffer().AsMemory(0, checked((int)message.Length)));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ForwardAllPackets(): {ex.Message}", LogSeverity.Danger);
            }
            finally
            {
                await SafeCloseAndCleanup(socket, connectionId);
            }
        }

        private static async Task<WebSocketMessageType> ReceiveMessage(
            WebSocket socket, byte[] buffer, MemoryStream message)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                // Shutdown owns the close handshake and its timeout. Cancelling this
                // receive with the background-task token would abort that handshake.
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    return WebSocketMessageType.Close;

                // Text messages are still drained to EndOfMessage, but never processed.
                if (result.MessageType == WebSocketMessageType.Binary)
                    message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return result.MessageType;
        }

        private static async Task ProcessPacket(
            WebSocket socket, string connectionId, ReadOnlyMemory<byte> message)
        {
            RoofWebSocketPacket? webSocketPacket = null;
            try
            {
                webSocketPacket = JsonSerializer.Deserialize(
                    message.Span,
                    RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket
                );
            }
            catch (Exception ex)
            {
                Logger.Log($"ForwardAllPackets(): Failed to deserialize packet. {ex.Message}", LogSeverity.Danger);
                return;
            }

            if (webSocketPacket == null ||
                string.IsNullOrEmpty(webSocketPacket.SenderIdentifier) ||
                string.IsNullOrEmpty(webSocketPacket.ForwardingToIdentifier) ||
                string.IsNullOrEmpty(webSocketPacket.Payload))
            {
                Logger.Log("Malformed packet detected. Dropping...", LogSeverity.Warning);
                return;
            }

            // Ensure this connection is bound to the announced SenderIdentifier
            await BindConnectionIfFirstPacket(connectionId, webSocketPacket.SenderIdentifier, socket);

            // Ignore circular routing
            if (webSocketPacket.SenderIdentifier.Equals(webSocketPacket.ForwardingToIdentifier, StringComparison.Ordinal))
            {
                Logger.Log("Circular routing detected. Dropping the packet...", LogSeverity.Warning);
                return;
            }

            // Heartbeat from client -> mark received
            if (webSocketPacket.ForwardingToIdentifier.Equals(Program.PATHFINDER_IDENTIFIER, StringComparison.Ordinal) &&
                webSocketPacket.Payload.Equals(WebSocketProtocolConstants.HEARTBEAT, StringComparison.Ordinal))
            {
                if (_clientsByIdentifier.TryGetValue(webSocketPacket.SenderIdentifier, out var pfClient))
                    pfClient.LastHeartbeatReceivedTime = DateTime.UtcNow;
                return;
            }

            // Forward if target exists
            if (_clientsByIdentifier.TryGetValue(webSocketPacket.ForwardingToIdentifier, out var target) &&
                target.Socket != null && target.Socket.State == WebSocketState.Open)
            {
                Logger.Log($"Client {webSocketPacket.SenderIdentifier} -> {webSocketPacket.ForwardingToIdentifier}");

                // Stamp the sender fingerprint with the handshake connection id.
                webSocketPacket.SenderFingerprint = connectionId;

                // Serialize the updated packet for forwarding.
                byte[] updatedPacket = JsonSerializer.SerializeToUtf8Bytes(
                    webSocketPacket,
                    RoofWebSocketPacketJSONContext.Default.RoofWebSocketPacket
                );

                await target.Socket.SendAsync(
                    new ArraySegment<byte>(updatedPacket),
                    WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            else
            {
                Logger.Log($"Target {webSocketPacket.ForwardingToIdentifier} not connected.", LogSeverity.Warning);
            }
        }

        private static async Task BindConnectionIfFirstPacket(string connectionId, string senderIdentifier, WebSocket socket)
        {
            if (!_connectionsById.TryRemove(connectionId, out var temp))
            {
                if (_clientsByIdentifier.TryGetValue(senderIdentifier, out var activeClient) &&
                    activeClient.ConnectionId == connectionId &&
                    ReferenceEquals(activeClient.Socket, socket))
                {
                    activeClient.Socket = socket;
                }

                return;
            }

            temp.Identifier = senderIdentifier;
            temp.Socket = socket;

            PathFinderClient? replacedClient = null;
            _clientsByIdentifier.AddOrUpdate(
                senderIdentifier,
                temp,
                (_, existingClient) =>
                {
                    if (!ReferenceEquals(existingClient, temp))
                        replacedClient = existingClient;

                    return temp;
                });

            if (replacedClient != null &&
                replacedClient.ConnectionId != connectionId &&
                !ReferenceEquals(replacedClient.Socket, socket))
            {
                Logger.Log(
                    $"Reconnect/session replacement for id={senderIdentifier}: oldConn={replacedClient.ConnectionId} newConn={connectionId} ({temp.OriginIP ?? "-"}) ray={temp.RayId ?? "-"}",
                    LogSeverity.Warning);

                _connectionsById.TryRemove(replacedClient.ConnectionId, out _);

                if (replacedClient.Socket != null)
                    await SafeCloseAndCleanup(replacedClient.Socket, replacedClient.ConnectionId);
            }
            else
            {
                // Use existing OriginIP/Ray (from handshake) for the log line
                Logger.Log($"Identify conn={connectionId} -> id={senderIdentifier} ({temp.OriginIP ?? "-"}) ray={temp.RayId ?? "-"}");
            }
        }

        private static async Task SafeCloseAndCleanup(WebSocket socket, string connectionId)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, WebSocketProtocolConstants.NORMAL_CLOSE_REASON, CancellationToken.None);
            }
            catch { /* ignore */ }

            // Remove from temp
            _connectionsById.TryRemove(connectionId, out _);

            // Remove any identified entries pointing to this socket
            foreach (var kvp in _clientsByIdentifier.ToArray())
            {
                if (ReferenceEquals(kvp.Value.Socket, socket) || kvp.Value.ConnectionId == connectionId)
                    _clientsByIdentifier.TryRemove(kvp);
            }
        }

        private static async Task CloseClientForShutdown(
            PathFinderClient client,
            string registryName,
            string registryKey,
            ShutdownCloseResults results,
            ConcurrentDictionary<string, byte> closedConnectionIds)
        {
            if (!closedConnectionIds.TryAdd(client.ConnectionId, 0))
                return;

            Interlocked.Increment(ref results.Attempted);

            var socket = client.Socket;
            if (socket == null)
            {
                Interlocked.Increment(ref results.AlreadyClosed);
                Logger.Log($"Shutdown close skipped for {registryName} socket key={registryKey} conn={client.ConnectionId}: socket is null.");
                return;
            }

            if (socket.State == WebSocketState.Closed || socket.State == WebSocketState.Aborted)
            {
                Interlocked.Increment(ref results.AlreadyClosed);
                Logger.Log($"Shutdown close skipped for {registryName} socket key={registryKey} conn={client.ConnectionId}: state={socket.State}.");
                return;
            }

            try
            {
                using var timeoutCts = new CancellationTokenSource(_shutdownGraceTimeout);

                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        SHUTDOWN_CLOSE_REASON,
                        timeoutCts.Token);
                }

                if (socket.State == WebSocketState.Closed)
                {
                    Interlocked.Increment(ref results.Closed);
                    Logger.Log($"Shutdown close completed for {registryName} socket key={registryKey} conn={client.ConnectionId}.");
                    return;
                }

                socket.Abort();
                Interlocked.Increment(ref results.Aborted);
                Logger.Log(
                    $"Shutdown close timed out for {registryName} socket key={registryKey} conn={client.ConnectionId}; aborted state={socket.State}.",
                    LogSeverity.Warning);
            }
            catch (OperationCanceledException)
            {
                AbortSocketAfterShutdownFailure(socket, client, registryName, registryKey, results, "timed out");
            }
            catch (Exception ex)
            {
                AbortSocketAfterShutdownFailure(socket, client, registryName, registryKey, results, ex.Message);
            }
        }

        private static void AbortSocketAfterShutdownFailure(
            WebSocket socket,
            PathFinderClient client,
            string registryName,
            string registryKey,
            ShutdownCloseResults results,
            string reason)
        {
            try
            {
                socket.Abort();
                Interlocked.Increment(ref results.Aborted);
                Logger.Log(
                    $"Shutdown close failed for {registryName} socket key={registryKey} conn={client.ConnectionId}: {reason}; socket aborted.",
                    LogSeverity.Warning);
            }
            catch (Exception abortEx)
            {
                Interlocked.Increment(ref results.Failed);
                Logger.Log(
                    $"Shutdown close failed for {registryName} socket key={registryKey} conn={client.ConnectionId}: {reason}; abort failed: {abortEx.Message}",
                    LogSeverity.Danger);
            }
        }

        private sealed class ShutdownCloseResults
        {
            public int Attempted;
            public int Closed;
            public int AlreadyClosed;
            public int Aborted;
            public int Failed;
        }

        private static Dictionary<string, string> ParseHeaders(string raw)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in raw.Split(new[] { HTTP_LINE_ENDING, "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf(':');
                if (idx > 0)
                {
                    var name = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim();
                    dict[name] = val;
                }
            }
            return dict;
        }
    }
}
