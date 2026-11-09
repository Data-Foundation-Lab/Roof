using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RoofData
{
    internal static class _JsonSerializationDefaults
    {
        internal const int MAX_DEPTH = 1000;
    }

    [JsonSourceGenerationOptions(WriteIndented = false, MaxDepth = _JsonSerializationDefaults.MAX_DEPTH)]
    [JsonSerializable(typeof(RoofDirectoryData))]
    public partial class RoofDirectoryDataJSONContext : JsonSerializerContext
    {
        public static RoofDirectoryDataJSONContext PreserveReference { get; } = new(options: new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            MaxDepth = _JsonSerializationDefaults.MAX_DEPTH,
            WriteIndented = false,
        });
    }

    [JsonSourceGenerationOptions(WriteIndented = false, MaxDepth = _JsonSerializationDefaults.MAX_DEPTH)]
    [JsonSerializable(typeof(RoofFileData))]
    public partial class RoofFileDataJSONContext : JsonSerializerContext
    {
        public static RoofFileDataJSONContext PreserveReference { get; } = new(options: new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            MaxDepth = _JsonSerializationDefaults.MAX_DEPTH,
            WriteIndented = false,
        });
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(List<string>))]
    public partial class ListOfStringJsonSerializerContext : JsonSerializerContext
    {
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(List<RoofDirectoryData>))]
    public partial class ListOfRoofDirectoryDataSerializerContext : JsonSerializerContext
    {
    }
}