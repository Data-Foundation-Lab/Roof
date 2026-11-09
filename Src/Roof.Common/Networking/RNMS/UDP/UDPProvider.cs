using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.UDP
{
    public class UDPProvider : IDisposable
    {
        private bool _isUnmanaged;

        private ManagedUDPProvider? _managedUDPProvider;
        private UnmanagedUDPProvider? _unmanagedUDPProvider;

        public delegate void MessageReceivedEventDelegate(IPEndPoint? clientAddress, string message);
        public event EventHandler<IPEndPoint> OnClientConnected;
        public event EventHandler<IPEndPoint> OnClientDisconnected;
        public event MessageReceivedEventDelegate OnMessageReceived;

        public int Port
        {
            get
            {
                if (_isUnmanaged && _unmanagedUDPProvider != null)
                    return _unmanagedUDPProvider.Port;

                if (!_isUnmanaged && _managedUDPProvider != null)
                    return _managedUDPProvider.Port;

                return 0;
            }
        }

        public UDPProvider(string destinationAddress, int destinationPort, bool isUnmanaged, bool isInListenerMode)
        {
            _isUnmanaged = isUnmanaged;

            if(isUnmanaged)
            {
                _unmanagedUDPProvider = new UnmanagedUDPProvider(destinationAddress, destinationPort, isInListenerMode);

                _unmanagedUDPProvider.ClientConnected += UnmanagedUDPProvider_ClientConnected;
                _unmanagedUDPProvider.MessageReceived += UnmanagedUDPProvider_MessageReceived;
                _unmanagedUDPProvider.ClientDisconnected += UnmanagedUDPProvider_ClientDisconnected;
            }
            else
            {
                _managedUDPProvider = new ManagedUDPProvider(destinationAddress, destinationPort, isInListenerMode);

                _managedUDPProvider.ClientConnected += ManagedUDPProvider_ClientConnected;
                _managedUDPProvider.MessageReceived += ManagedUDPProvider_MessageReceived;
                _managedUDPProvider.ClientDisconnected += ManagedUDPProvider_ClientDisconnected;
            }
        }

        private void UnmanagedUDPProvider_ClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(this, clientAddress);
        }

        private void UnmanagedUDPProvider_ClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(this, clientAddress);
        }

        private void UnmanagedUDPProvider_MessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }

        private void ManagedUDPProvider_ClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(this, clientAddress);
        }

        private void ManagedUDPProvider_ClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(this, clientAddress);
        }

        private void ManagedUDPProvider_MessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }

        public async Task<bool> SendMessage(string message)
        {
            if (_isUnmanaged)
            {
                return await _unmanagedUDPProvider.SendMessageUnreliable(message);
            }
            else
            {
                return await _managedUDPProvider.SendMessage(message);
            }
        }

        public async Task<bool> SendMessage(string message, IPEndPoint endPoint)
        {
            if (_isUnmanaged)
            {
                return await _unmanagedUDPProvider.SendMessageUnreliable(message, endPoint);
            }
            else
            {
                return await _managedUDPProvider.SendMessage(message, endPoint);
            }
        }

        public async Task<bool> SendMessage(byte[] messageBuffer)
        {
            if (_isUnmanaged)
            {
                return await _unmanagedUDPProvider.SendMessageUnreliable(messageBuffer);
            }
            else
            {
                return await _managedUDPProvider.SendMessage(messageBuffer);
            }
        }

        public async Task<bool> SendMessage(byte[] messageBuffer, IPEndPoint endpoint)
        {
            if (_isUnmanaged)
            {
                return await _unmanagedUDPProvider.SendMessageUnreliable(messageBuffer, endpoint);
            }
            else
            {
                return await _managedUDPProvider.SendMessage(messageBuffer, endpoint);
            }
        }

        public void Dispose() 
        {
            if(_isUnmanaged && _unmanagedUDPProvider != null)
            {
                _unmanagedUDPProvider.Dispose();
            }

            if(_isUnmanaged == false &&  _managedUDPProvider != null)
            {
                _managedUDPProvider.Dispose();
            }
        }
    }
}
