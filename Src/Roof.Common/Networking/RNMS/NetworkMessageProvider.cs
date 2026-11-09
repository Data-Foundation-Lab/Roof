using Roof.Common.Networking.RNMS.UDP;
using Roof.Common.Networking.RNMS.WebSocket;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Roof.Common.Networking.RNMS.TCP;

namespace Roof.Common.Networking.RNMS
{
    public enum RNMSNetworkProtocols
    {
        MANAGED_TCP,
        UNMANAGED_UDP,
        MANAGED_UDP,
        WEBSOCKET_TCP
    }

    public class NetworkMessageProvider : IDisposable
    {
        private RNMSNetworkProtocols _baseProtocol;

        private UDPProvider? _udpProvider = null;
        private TCPProvider? _tcpProvider = null;
        private WebSocketProvider? _webSocketProvider = null;

        public event EventHandler<IPEndPoint> OnClientConnected;
        public event EventHandler<IPEndPoint> OnClientDisconnected;
        public event EventHandler<string> OnWebSocketClientConnected;
        public event EventHandler<string> OnWebSocketClientDisconnected;

        public delegate void MessageReceivedEventDelegate(IPEndPoint? clientAddress, string message);
        public delegate void WebSocketMessageReceivedEventDelegate(RoofWebSocketPacket message);

        public event MessageReceivedEventDelegate OnMessageReceived;
        public event WebSocketMessageReceivedEventDelegate OnWebSocketMessageReceived;

        public Task<bool> Initialize(RNMSNetworkProtocols baseProtocol, bool isInListenerMode, string destinationAddress, int destinationPort = 0)
        {
            return Initialize(baseProtocol, isInListenerMode, destinationAddress, destinationPort, CancellationToken.None);
        }

        public async Task<bool> Initialize(RNMSNetworkProtocols baseProtocol, bool isInListenerMode, string destinationAddress, int destinationPort, CancellationToken cancellationToken)
        {
            try
            {
                _baseProtocol = baseProtocol;
                
                if (baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
                {
                    _tcpProvider = new TCPProvider(destinationAddress, destinationPort, false, isInListenerMode);
                
                    _tcpProvider.OnClientConnected += TCPProvider_OnClientConnected;
                    _tcpProvider.OnClientDisconnected += TCPProvider_OnClientDisconnected;
                    _tcpProvider.OnMessageReceived += TCPProvider_OnMessageReceived;

                    return true;
                }

                if(_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP)
                {
                    _udpProvider = new UDPProvider(destinationAddress, destinationPort, true, isInListenerMode);

                    return true;
                }

                if(_baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
                {
                    _udpProvider = new UDPProvider(destinationAddress, destinationPort, false, isInListenerMode);

                    _udpProvider.OnClientConnected += UDPProvider_OnClientConnected;
                    _udpProvider.OnClientDisconnected += UDPProvider_OnClientDisconnected;
                    _udpProvider.OnMessageReceived += UDPProvider_OnMessageReceived;

                    return true;
                }

                if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
                {
                    _webSocketProvider = new WebSocketProvider();

                    _webSocketProvider.ClientConnected += WebSocketProvider_ClientConnected;
                    _webSocketProvider.ClientDisconnected += WebSocketProvider_ClientDisconnected;
                    _webSocketProvider.MessageReceived += WebSocketProvider_MessageReceived;

                    return await _webSocketProvider.Initialize(new Uri(destinationAddress), cancellationToken);
                }

                return false;
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.Message);
                return false;
            }
        }

        public object GetProvider()
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
                return _tcpProvider;
            else if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
                return _udpProvider;
            else if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
                return _webSocketProvider;
            else
                return null;
        }

        public async Task<bool> SendMessage(string message)
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
            {
                return await _tcpProvider.SendMessage(message);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
            {
                return await _udpProvider.SendMessage(message);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
            {
                return await _webSocketProvider.SendAsync(message);
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> SendMessage(string message, IPEndPoint endpoint)
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
            {
                return await _tcpProvider.SendMessage(message, endpoint);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
            {
                return await _udpProvider.SendMessage(message, endpoint);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
            {
                return await _webSocketProvider.SendAsync(message);
            }
            else
                return false;
        }

        public async Task<bool> SendMessage(byte[] messageBuffer)
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
            {
                return await _tcpProvider.SendMessage(messageBuffer);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
            {
                return await _udpProvider.SendMessage(messageBuffer);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
            {
                return await _webSocketProvider.SendAsync(messageBuffer);
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> SendMessage(byte[] messageBuffer, IPEndPoint endpoint)
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP)
            {
                return await _tcpProvider.SendMessage(messageBuffer, endpoint);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP)
            {
                return await _udpProvider.SendMessage(messageBuffer, endpoint);
            }
            else if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP)
            {
                return await _webSocketProvider.SendAsync(messageBuffer);
            }
            else
            {
                return false;
            }
        }
        
        private void TCPProvider_OnClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(sender, clientAddress);
        }
        
        private void TCPProvider_OnClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(sender, clientAddress);
        }
        
        private void TCPProvider_OnMessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }

        private void UDPProvider_OnClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(sender, clientAddress);
        }

        private void UDPProvider_OnClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(sender, clientAddress);
        }

        private void UDPProvider_OnMessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }

        private void WebSocketProvider_ClientConnected(object? sender, string clientIdentifier)
        {
            OnWebSocketClientConnected?.Invoke(sender, clientIdentifier);
        }

        private void WebSocketProvider_ClientDisconnected(object? sender, string clientIdentifier)
        {
            OnWebSocketClientDisconnected?.Invoke(sender, clientIdentifier);
        }

        private void WebSocketProvider_MessageReceived(RoofWebSocketPacket message)
        {
            OnWebSocketMessageReceived?.Invoke(message);
        }

        public void Dispose()
        {
            if (_baseProtocol == RNMSNetworkProtocols.MANAGED_TCP && _tcpProvider != null)
            {
                _tcpProvider.Dispose();
            }
            
            if (_baseProtocol == RNMSNetworkProtocols.UNMANAGED_UDP && _udpProvider != null
                || _baseProtocol == RNMSNetworkProtocols.MANAGED_UDP && _udpProvider != null)
            {
                _udpProvider.Dispose();
            }

            if (_baseProtocol == RNMSNetworkProtocols.WEBSOCKET_TCP && _webSocketProvider != null)
            {
                _webSocketProvider.Dispose();
            }
        }
    }
}
