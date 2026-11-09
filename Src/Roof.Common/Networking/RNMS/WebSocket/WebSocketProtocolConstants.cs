namespace Roof.Common.Networking.RNMS.WebSocket;

/// <summary>
/// Wire-protocol values shared by WebSocket clients and relays.
/// </summary>
public static class WebSocketProtocolConstants
{
    public const string HEARTBEAT = "WS_HEARTBEAT";
    public const string HEARTBEAT_TIMEOUT_CLOSE_REASON = HEARTBEAT + " time out.";
    public const string NORMAL_CLOSE_REASON = "Closing";
}
