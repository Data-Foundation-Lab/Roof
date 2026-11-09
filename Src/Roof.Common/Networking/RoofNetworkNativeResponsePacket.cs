using System.Text.Json.Serialization;
using System.Text.Json;
using System.Linq;

namespace Roof.Common.Networking
{
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(RoofNetworkNativeResponsePacket))]
    public partial class RoofNetworkNativeResponsePacketJSONContext : JsonSerializerContext
    {
        
    }

    //It should be a struct, but due to NativeAOT, the JSON needs to be in class format.
    //This is because we are not depending on JSON.NET (Newtonsoft), but rather on BCLs.
    public partial class RoofNetworkNativeResponsePacket 
    {
        public bool Status { get; set; }
        public List<string> Responses { get; set; } = null;

        public void AddResponse(string response)
        {
            //Only initialise the array if we're actually using it.
            Responses ??= new List<string>();

            if (!Responses.Contains(response))
                Responses.Add(response);
        }
    }
}