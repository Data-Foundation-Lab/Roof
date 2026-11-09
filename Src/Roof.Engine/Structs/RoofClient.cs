using System.Net;
using Roof.Common.Tools;
using Roof.Engine.Managers;

namespace Roof.Engine.Structs
{
    public class RoofClient : IDisposable
    {
        private readonly object _stateLock = new();
        internal SemaphoreSlim CommandGate { get; } = new(1, 1);

        public DateTime ConnectedTime { get; } = DateTime.UtcNow;
        public string RegistrationClientID { get; set; }
        public bool IsAdditionalRegistration { get; set; }
        public bool IsDisposed { get; private set; }

        public string ClientID { get; set; }
        public IPEndPoint? EndPoint { get; set; }
        public string GlobalRoutedFingerprint { get; set; }
        public bool IsInRemoteMode { get; set; }
        public bool IsVerified { get; set; }
        public bool IsFirstTimeTCPEncryptionInfoSent { get; set; }
        public bool IsNounceVerified;
        public string FirstTimeTCPEncryptionKey { get; set; } //Should be cleared after use
        public string FirstTimeTCPEncryptionIV { get; set; } //Should be cleared after use
        public string Nounce { get; set; } //Should be cleared after use
        public ClientFileSystemManager FSManager { get; set; }
        public DateTime? LastPFHeartbeatSentTime { get; set; }
        public DateTime? LastPFHeartbeatReceivedTime { get; set; }

        private (string?, string?) _encryptionInfo;

        public void WithStateLock(Action<RoofClient> update)
        {
            lock (_stateLock)
            {
                update(this);
            }
        }

        public T WithStateLock<T>(Func<RoofClient, T> read)
        {
            lock (_stateLock)
            {
                return read(this);
            }
        }

        public void SetEncryptionInfo(string encryptionKey, string encryptionIV)
        {
            WithStateLock(client => client._encryptionInfo = (encryptionKey, encryptionIV));
        }

        public async Task<(string?, string?)> GetEncryptionInfo()
        {
            try
            {
                if (WithStateLock(c => c.IsDisposed))
                    return (null, null);
                var cachedInfo = WithStateLock(client =>
                {
                    if (client.FirstTimeTCPEncryptionKey != null && client.FirstTimeTCPEncryptionIV != null)
                        return (client.FirstTimeTCPEncryptionKey, client.FirstTimeTCPEncryptionIV);

                    if (client._encryptionInfo.Item1 != null && client._encryptionInfo.Item2 != null)
                        return client._encryptionInfo;

                    return (null, null);
                });

                if (cachedInfo.Item1 != null && cachedInfo.Item2 != null)
                    return cachedInfo;

                if (string.IsNullOrEmpty(ClientID))
                    return (null, null);

                var config = ConfigurationManager.GetConfiguration();

                if(!config.RegisteredClients.ContainsKey(ClientID))
                    return (null, null);

                if(!File.Exists(ConfigurationManager.ClientKeyPath(ClientID)))
                    return (null, null);
                
                var readClientNEKContent = await File.ReadAllBytesAsync(ConfigurationManager.ClientKeyPath(ClientID));
                
                if (readClientNEKContent.Length != 48)
                    return (null, null);

                byte[] readClientNEK = readClientNEKContent.Take(32).ToArray();  // AES-256 = 32 bytes
                byte[] readClientNIV = readClientNEKContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                string loadedClientNEK = Convert.ToBase64String(readClientNEK);
                string loadedClientNIV = Convert.ToBase64String(readClientNIV);

                return WithStateLock(client => client.IsDisposed ? (null, null) :
                    client._encryptionInfo = (loadedClientNEK, loadedClientNIV));
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[RoofClient] - GetEncryptionInfo(): Failed to get encryption info.", ex);
                return (null, null);
            }
        }

        public void Dispose()
        {
            WithStateLock(client =>
            {
                client.IsDisposed = true;
                client.IsVerified = false;
                client.IsNounceVerified = false;
                client.IsFirstTimeTCPEncryptionInfoSent = false;
                client.RegistrationClientID = null;
                client.FirstTimeTCPEncryptionKey = null;
                client.FirstTimeTCPEncryptionIV = null;
                client.Nounce = null;
                client._encryptionInfo = (null, null);
                client.FSManager = null;
            });
        }
    }
}
