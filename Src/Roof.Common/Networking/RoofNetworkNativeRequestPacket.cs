using System.Text.Json.Serialization;
using System.Text.Json;
using System.Linq;

namespace Roof.Common.Networking
{
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(RoofNetworkNativeRequestPacket))]
    public partial class RoofNetworkNativeRequestPacketJSONContext : JsonSerializerContext
    {
        
    }

    //It should be a struct, but due to NativeAOT, the JSON needs to be in class format.
    //This is because we are not depending on JSON.NET (Newtonsoft), but rather on BCLs.
    public partial class RoofNetworkNativeRequestPacket 
    {
        public string? ClientID { get; set; }
        public string? RoofIP { get; set; }
        public List<string>? Requests { get; set; } = null;
    }
}