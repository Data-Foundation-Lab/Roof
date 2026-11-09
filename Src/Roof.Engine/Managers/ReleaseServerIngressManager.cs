using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Roof.Common.Tools;

namespace Roof.Engine.Managers
{
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(JsonObject))]
    public partial class ReleaseChannelVersioningJSONContext : JsonSerializerContext
    {   
    }
    
    public class ReleaseServerIngressManager
    {
        private const string RELEASE_CHANNEL_BASE_URL = @"RELEASE_CHANNEL_BASE_URL";

        public static async Task<string?> GetLatestVersion()
        {
            try
            {
                using var httpClient = new HttpClient();
    #if IA || DEBUG
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/ia/artifact_versions.json");
    #endif
    #if BETA
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/beta/artifact_versions.json");
    #endif
    #if RELEASE
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/pa/artifact_versions.json");
    #endif

                var rtObj = JsonSerializer.Deserialize(jsonString, ReleaseChannelVersioningJSONContext.Default.JsonObject);

                if (rtObj == null || !rtObj.TryGetPropertyValue("artifacts", out var artifactsRootObj))
                {
                    Logger.LogToFile("[ReleaseServerIngressManager]: Failed to read artifacts.", LogSeverity.Warning);
                    return null;
                }

                var artifactsArray = artifactsRootObj?.AsArray();
                var roofEngineObj = artifactsArray?
                    .Select(n => n?.AsObject())
                    .Where(o => o is not null)
                    .SelectMany(o => o!)                        // iterate KVPs inside each object
                    .Where(kvp => kvp.Key == "RoofEngine")
                    .Select(kvp => kvp.Value?.AsObject())
                    .FirstOrDefault();

                if (roofEngineObj is null)
                {
                    Logger.LogToFile("[ReleaseServerIngressManager]: Failed to read engine artifact entry.", LogSeverity.Warning);
                    return null;
                }
                
                return roofEngineObj?["Version"]?.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ReleaseServerIngressManager]: Exception occurred in GetLatestVersion().", ex);
                return null;
            }
        }
        
        public static async Task<string?> GetOfficialEngineHash()
        {
            try
            {
                using HttpClient httpClient = new HttpClient();
    #if IA || DEBUG
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/ia/artifact_versions.json");
    #endif
    #if BETA
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/beta/artifact_versions.json");
    #endif
    #if RELEASE
                var jsonString = await httpClient.GetStringAsync($"{RELEASE_CHANNEL_BASE_URL}/pa/artifact_versions.json");
    #endif

                var rtObj = JsonSerializer.Deserialize(jsonString, ReleaseChannelVersioningJSONContext.Default.JsonObject);
                if (rtObj == null || !rtObj.TryGetPropertyValue("artifacts", out var artifactsRootObj))
                {
                    Logger.LogToFile("[ReleaseServerIngressManager]: Failed to read official version metadata.");
                    return null;
                }

                var artifactsArray = artifactsRootObj?.AsArray();
                
                var roofEngineObj = artifactsArray?
                    .Select(n => n?.AsObject())
                    .Where(o => o is not null)
                    .SelectMany(o => o!)                        // iterate KVPs inside each object
                    .Where(kvp => kvp.Key == "RoofEngine")
                    .Select(kvp => kvp.Value?.AsObject())
                    .FirstOrDefault();

                if (roofEngineObj is null)
                {
                    Logger.LogToFile("[ReleaseServerIngressManager]: Failed to read engine artifact entry.", LogSeverity.Warning);
                    return null;
                }

                if (OperatingSystem.IsLinux())
                {
                    return roofEngineObj?["LINUX_ARM64_Hash"]?.ToString();
                }
                else
                {
                    Logger.LogToFile("[ReleaseServerIngressManager]: UNSUPPORTED PLATFORM for hash validation.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ReleaseServerIngressManager]: Exception occurred in GetOfficialEngineHash().", ex);
                return null;
            }
        }
        
        public static (string downloadUrl, string appExtension) GetEngineDownloadInfo(string latestVersion)
        {
            if (OperatingSystem.IsLinux())
            {
#if IA || DEBUG
                return ($"{RELEASE_CHANNEL_BASE_URL}/ia/roofengine/{latestVersion}/LINUX_ARM64/Roof.Engine", "");
#endif
#if BETA
                return ($"{RELEASE_CHANNEL_BASE_URL}/beta/roofengine/{latestVersion}/LINUX_ARM64/Roof.Engine", "");
#endif
#if RELEASE
                return ($"{RELEASE_CHANNEL_BASE_URL}/pa/roofengine/{latestVersion}/LINUX_ARM64/Roof.Engine", "");
#endif
            }
            else
            {
                throw new PlatformNotSupportedException("UNSUPPORTED PLATFORM for downloading binary.");
            }
        }
    }
}