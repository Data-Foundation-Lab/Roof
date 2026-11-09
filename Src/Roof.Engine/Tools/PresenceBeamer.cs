using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using Roof.Common.Tools;

namespace Roof.Engine.Tools
{
    internal class PresenceBeamer
    {
        public static bool CastPresenceBeam(string clientIP)
        {
            if(ICMPPingClient(clientIP))
            {
#if DEBUG
                Logger.Log($"Client: {clientIP} is reachable.");
#endif

                // Send a presence beam to the client...

                return true;
            }
            else
            {
#if DEBUG
                Logger.Log($"Client: {clientIP} is unreachable.");
#endif
                return false;
            }
        }

        private static bool ICMPPingClient(string clientIP)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(clientIP, 5000); // 5-second timeout
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (PingException)
            {
                return false;
            }
        }
    }
}