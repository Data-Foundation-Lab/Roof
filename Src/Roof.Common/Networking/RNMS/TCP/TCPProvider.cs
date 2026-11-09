using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.TCP
{
    public class TCPProvider : IDisposable
    {
        private bool _isUnmanaged;

        private ManagedTCPProvider? _managedTCPProvider;

        public delegate void MessageReceivedEventDelegate(IPEndPoint? clientAddress, string message);
        public event EventHandler<IPEndPoint> OnClientConnected;
        public event EventHandler<IPEndPoint> OnClientDisconnected;
        public event MessageReceivedEventDelegate OnMessageReceived;

        public int Port
        {
            get
            {
                /*if (_isUnmanaged && _unmanagedTCPProvider != null)
                    return _unmanagedTCPProvider.Port;*/

                if (!_isUnmanaged && _managedTCPProvider != null)
                    return _managedTCPProvider.Port;

                return 0;
            }
        }

        public TCPProvider(string destinationAddress, int destinationPort, bool isUnmanaged, bool isInListenerMode)
        {
            _isUnmanaged = isUnmanaged;

            if(isUnmanaged)
            {
                /*_unmanagedTCPProvider = new UnmanagedTCPProvider(destinationAddress, destinationPort, isInListenerMode);

                _unmanagedTCPProvider.ClientConnected += UnmanagedTCPProvider_ClientConnected;
                _unmanagedTCPProvider.MessageReceived += UnmanagedTCPProvider_MessageReceived;
                _unmanagedTCPProvider.ClientDisconnected += UnmanagedTCPProvider_ClientDisconnected;*/
            }
            else
            {
                _managedTCPProvider = new ManagedTCPProvider(destinationAddress, destinationPort, isInListenerMode);

                _managedTCPProvider.ClientConnected += ManagedTCPProvider_ClientConnected;
                _managedTCPProvider.MessageReceived += ManagedTCPProvider_MessageReceived;
                _managedTCPProvider.ClientDisconnected += ManagedTCPProvider_ClientDisconnected;
            }
        }

        /*private void UnmanagedTCPProvider_ClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(this, clientAddress);
        }

        private void UnmanagedTCPProvider_ClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(this, clientAddress);
        }

        private void UnmanagedTCPProvider_MessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }*/

        private void ManagedTCPProvider_ClientConnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientConnected?.Invoke(this, clientAddress);
        }

        private void ManagedTCPProvider_ClientDisconnected(object? sender, IPEndPoint clientAddress)
        {
            OnClientDisconnected?.Invoke(this, clientAddress);
        }

        private void ManagedTCPProvider_MessageReceived(IPEndPoint? clientAddress, string message)
        {
            OnMessageReceived?.Invoke(clientAddress, message);
        }

        public async Task<bool> SendMessage(string message)
        {
            if (_isUnmanaged)
            {
                //return await _unmanagedTCPProvider.SendMessageUnreliable(message);
                return false;
            }
            else
            {
                return await _managedTCPProvider.SendMessage(message);
            }
        }

        public async Task<bool> SendMessage(string message, IPEndPoint endPoint)
        {
            if (_isUnmanaged)
            {
                //return await _unmanagedTCPProvider.SendMessageUnreliable(message, endPoint);
                return false;
            }
            else
            {
                return await _managedTCPProvider.SendMessage(message, endPoint);
            }
        }

        public async Task<bool> SendMessage(byte[] messageBuffer)
        {
            if (_isUnmanaged)
            {
                //return await _unmanagedTCPProvider.SendMessageUnreliable(messageBuffer);
                return false;
            }
            else
            {
                return await _managedTCPProvider.SendMessage(messageBuffer);
            }
        }

        public async Task<bool> SendMessage(byte[] messageBuffer, IPEndPoint endpoint)
        {
            if (_isUnmanaged)
            {
                //return await _unmanagedTCPProvider.SendMessageUnreliable(messageBuffer, endpoint);
                return false;
            }
            else
            {
                return await _managedTCPProvider.SendMessage(messageBuffer, endpoint);
            }
        }

        public void Dispose() 
        {
            /*if(_isUnmanaged && _unmanagedTCPProvider != null)
            {
                _unmanagedTCPProvider.Dispose();
            }*/

            if(_isUnmanaged == false &&  _managedTCPProvider != null)
            {
                _managedTCPProvider.Dispose();
            }
        }
    }
}
