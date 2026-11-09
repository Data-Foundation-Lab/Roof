namespace Roof.Common.Networking.RNMS;

public static class RNMSDefaults
{
    public static readonly TimeSpan DisconnectionTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HeartbeatPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan UdpDisconnectionPollInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan UdpRetransmissionPollInterval = TimeSpan.FromMilliseconds(100);
}
