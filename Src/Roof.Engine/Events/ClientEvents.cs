using System.Net;
using System.Net.Sockets;
using Roof.Engine.Managers;
using Roof.Common.Tools;
using Roof.Engine.Structs;

namespace Roof.Engine.Events
{
    public class ClientEvents
    {
        public delegate Task ClientEventTemplate(RoofClient rfClient);

        public static event ClientEventTemplate OnClientConnected;
        public static event ClientEventTemplate OnClientDisconnected;
        public static event ClientEventTemplate OnClientMessageReceived;

        public static async Task InvokeOnClientConnected(RoofClient rfClient)
        {
            try
            {
                if(OnClientConnected?.GetInvocationList().Length != 0 && OnClientConnected != null)
                {
                    await OnClientConnected(rfClient).ConfigureAwait(false);
                }
            }
            catch(Exception ex)
            {
                Logger.LogExceptionToFile($"[ClientEvents] - InvokeOnClientConnected(): Failed to invoke OnClientConnected subscribers.", ex);
            }
        }
    }
}
