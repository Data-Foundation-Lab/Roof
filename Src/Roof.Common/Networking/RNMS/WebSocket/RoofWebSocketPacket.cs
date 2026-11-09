using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RNMS.WebSocket
{
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(RoofWebSocketPacket))]
    public partial class RoofWebSocketPacketJSONContext : JsonSerializerContext
    {

    }

    //It should be a struct, but due to NativeAOT, the JSON needs to be in class format.
    //This is because we are not depending on JSON.NET (Newtonsoft), but rather on BCLs.
    public partial class RoofWebSocketPacket
    {
        public string? SenderIdentifier { get; set; }
        public string? ForwardingToIdentifier { get; set; }
        public string? SenderFingerprint { get; set; }
        public string? Payload { get; set; }
    }
}
