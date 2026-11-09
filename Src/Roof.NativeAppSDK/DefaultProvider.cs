using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Roof.Common.Networking;
using Roof.Common.Networking.RNMS;
using Roof.Common.Networking.RoofData;
using Roof.Common.Tools;

namespace Roof.NativeAppSDK;

public class DefaultProvider
{
    /// <summary>
    /// Local loopback Roof Client UDP endpoint.
    /// </summary>
    private const string DEFAULT_LOCAL_ROOFCLIENT_UDP_ENDPOINT_ADDRESS = "127.0.0.1";
    private const int DEFAULT_LOCAL_ROOFCLIENT_UDP_PORT = 8672;

    private static IPEndPoint _localRoofClientEndPoint =
        new IPEndPoint(IPAddress.Parse(DEFAULT_LOCAL_ROOFCLIENT_UDP_ENDPOINT_ADDRESS),
            DEFAULT_LOCAL_ROOFCLIENT_UDP_PORT);

    private static NetworkMessageProvider? _defaultLocalUDPNetworkMessageProvider;
    
    private static bool _isSDKInitialized;
    private static bool _isRoofDeckConnected = false;
    private static bool _isUserLoggedInToRoof = false;

    public static async Task<bool> Initialize()
    {
        if (_isSDKInitialized == false)
        {
            _defaultLocalUDPNetworkMessageProvider = new NetworkMessageProvider();
             
            _defaultLocalUDPNetworkMessageProvider.OnClientConnected += DefaultLocalUDPNetworkMessageProviderOnOnClientConnected;
            _defaultLocalUDPNetworkMessageProvider.OnClientDisconnected += DefaultLocalUDPNetworkMessageProviderOnOnClientDisconnected;
            _defaultLocalUDPNetworkMessageProvider.OnMessageReceived += DefaultLocalUDPNetworkMessageProviderOnOnMessageReceived;

            await _defaultLocalUDPNetworkMessageProvider.Initialize(RNMSNetworkProtocols.MANAGED_UDP, true, IPAddress.Any.ToString(), 0);

            if (IsRoofDeckRunning())
            {
                await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.SerializedLoggedInStatusCheckPacketBuffer,
                    _localRoofClientEndPoint);

                int waitedSecond = 0;
                while (_isUserLoggedInToRoof == false && waitedSecond < 10)
                {
                    await Task.Delay(1000);
                    waitedSecond++;
                }
            }
        
            _isSDKInitialized = true;
        }

        return true;
    }
    
    public static bool IsRoofDeckRunning()
    {
        try
        {
            //This part of the code shouldn't be in SDK codebase publicly.
#if DEBUG
            var foundRoofDeckProcesses = Process.GetProcessesByName("Roof.ClientTestConsole");
#endif
#if RELEASE || BETA || IA
        var foundRoofDeckProcesses = Process.GetProcessesByName("RoofDeck");
#endif
        
            return foundRoofDeckProcesses.Length == 1;
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public static bool IsUserLoggedInToRoof() => _isUserLoggedInToRoof;

    public static async Task<bool> RequestUserBaseDirectory()
    {
        try
        {
            if (IsRoofDeckRunning() == false || _isRoofDeckConnected == false || _isUserLoggedInToRoof == false ||
                _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.SerializedGetUserBaseDirectoryPacketBuffer,
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public static async Task<bool> RequestDirectory(string hashedPath)
    {
        try
        {
            if (IsRoofDeckRunning() == false || _isRoofDeckConnected == false || _isUserLoggedInToRoof == false ||
                _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
               return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructGetDirectoryPacketBuffer(hashedPath), 
                   _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public static async Task<bool> RequestDirectoryCreation(RoofDirectoryData roofDirectoryData)
    {
        try
        {
            if (IsRoofDeckRunning() == false || _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                var serializedRoofDirectoryData = JsonSerializer.Serialize(roofDirectoryData, RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData);
            
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructCreateDirectoryPacketBuffer(serializedRoofDirectoryData), 
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public static async Task<bool> RequestDirectoryUpdate(string updatingDirectoryHashedPath, RoofDirectoryData updatedRoofDirectoryData)
    {
        try
        {
            if (IsRoofDeckRunning() == false || _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                var serializedRoofDirectoryData = JsonSerializer.Serialize(updatedRoofDirectoryData, RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData);
            
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructUpdateDirectoryPacketBuffer(updatingDirectoryHashedPath, serializedRoofDirectoryData),
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public static async Task<bool> RequestDirectoryCopy(string updatingDirectoryHashedPath, string targetDirectoryHashedPath)
    {
        try
        {
            if (IsRoofDeckRunning() == false || _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructCopyDirectoryPacketBuffer(updatingDirectoryHashedPath, targetDirectoryHashedPath),
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    public static async Task<bool> RequestDirectoryDeletion(string hashedPath)
    {
        try
        {
            if (IsRoofDeckRunning() == false ||  _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructDeleteDirectoryPacketBuffer(hashedPath), _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }

    //This should be creating a raw file but with empty content. We'll be using UPDATE_FILE to write the data instead.
    public static async Task<bool> RequestFileCreation(RoofFileData fileData)
    {
        try
        {
            if (IsRoofDeckRunning() == false ||  _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                string serialisedFileData = JsonSerializer.Serialize(fileData, RoofFileDataJSONContext.PreserveReference.RoofFileData);
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructCreateFilePacketBuffer(serialisedFileData),
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public static async Task<bool> RequestFileUpdate(RoofFileData fileData)
    {
        try
        {
            if (IsRoofDeckRunning() == false ||  _isRoofDeckConnected == false || _isUserLoggedInToRoof == false || _defaultLocalUDPNetworkMessageProvider == null)
                return false;
            else
            {
                if (fileData.Data.Length > 0)
                {
                    //SHOULD BE CHECKING FOR MAX_FILE_DATA_SIZE => 65000 bytes (0.065 MB)
                    const int MAX_FILE_DATA_SIZE = 65000;

                    if (fileData.Data.Length > MAX_FILE_DATA_SIZE)
                        return false;
                }

                string serialisedFileData = JsonSerializer.Serialize(fileData, RoofFileDataJSONContext.PreserveReference.RoofFileData);
                return await _defaultLocalUDPNetworkMessageProvider.SendMessage(CommandPacketManager.ConstructUpdateFilePacketBuffer(fileData.HashedPath, serialisedFileData),
                    _localRoofClientEndPoint);
            }
        }
        catch (Exception e)
        {
            return false;
        }
    }
    
    private static void DefaultLocalUDPNetworkMessageProviderOnOnClientConnected(object? sender, IPEndPoint e)
    {
        _isRoofDeckConnected = true;
    }
    
    private static void DefaultLocalUDPNetworkMessageProviderOnOnClientDisconnected(object? sender, IPEndPoint e)
    {
        _isRoofDeckConnected = false;
        _isUserLoggedInToRoof = false;
    }
    
    private static void DefaultLocalUDPNetworkMessageProviderOnOnMessageReceived(IPEndPoint? clientaddress, string message)
    {
        RoofNetworkNativeResponsePacket? respPacket;
        try
        {
            respPacket = JsonSerializer.Deserialize(message, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
        }
        catch(Exception ex)
        {
            Logger.LogException("Exception occurred while trying to deserialize response packet.", ex);
            return;
        }

        if (respPacket.Responses == null || respPacket.Responses.Count == 0)
            return;

        var firstResponseMessage = respPacket.Responses.First();

        if(firstResponseMessage.Equals("USER_LOGIN_STATUS_RESULT"))
            _isUserLoggedInToRoof = respPacket.Status;

        if (firstResponseMessage.Equals("GET_USER_BASE_DIRECTORY_RESULT"))
        {
            if(respPacket.Status == true)
            {
                try
                {
                    //Attempt deserialization
                    var deserializedDirectoryData = JsonSerializer.Deserialize(respPacket.Responses[1], RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData);

                    if (deserializedDirectoryData != null)
                        Events.RoofDirectoryEvents.InvokeOnRoofBaseDirectoryAcquired(deserializedDirectoryData.HashedPath,
                            deserializedDirectoryData);
                }
                catch (Exception e)
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofBaseDirectoryAcquireFailed(e.Message);
                }
            }
            else
            {
                string reason = respPacket.Responses[1];
                Events.RoofDirectoryEvents.InvokeOnRoofBaseDirectoryAcquireFailed(reason);
            }
        }

        if (firstResponseMessage.Equals("GET_DIRECTORY_RESULT"))
        {
            string jsonString = respPacket.Responses[1];

            if (respPacket.Status == true)
            {
                RoofDirectoryData? directoryData;
                try
                {
                    directoryData = JsonSerializer.Deserialize(jsonString, RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData
                   );

                    if (directoryData != null)
                        Events.RoofDirectoryEvents.InvokeOnRoofDirectoryAcquired(directoryData.HashedPath, directoryData);
                }
                catch (Exception ex)
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryAcquireFailed(jsonString, ex.Message);
                }
            }
            else
            {
                if (Security.XxHash64.IsValidXxHash64Format(jsonString))
                {
                    string reason = respPacket.Responses[2];

                    if (string.IsNullOrEmpty(reason) == false)
                        Events.RoofDirectoryEvents.InvokeOnRoofDirectoryAcquireFailed(jsonString, reason);
                }
            }
        }

        if (firstResponseMessage.Equals("CREATE_DIRECTORY_RESULT"))
        {
            string hashedPath = respPacket.Responses[1];

            if (respPacket.Status == true)
            {
                if (Security.XxHash64.IsValidXxHash64Format(hashedPath))
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryCreated(hashedPath);
                }
            }
            else
            {
                //FAILED
            }
        }

        if (firstResponseMessage.Equals("UPDATE_DIRECTORY_RESULT"))
        {
            string hashedPath = respPacket.Responses[1];

            if (Security.XxHash64.IsValidXxHash64Format(hashedPath))
            {
                if (respPacket.Status == true)
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryUpdated(hashedPath);
                }
                else
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryUpdateFailed(hashedPath);
                }
            }
        }
        
        if (firstResponseMessage.Equals("COPY_DIRECTORY_RESULT"))
        {
            string hashedPath = respPacket.Responses[1];
            if (Security.XxHash64.IsValidXxHash64Format(hashedPath))
            {
                if (respPacket.Status == true)
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryCopied(hashedPath);
                }
                else
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryCopyFailed(hashedPath);
                }
            }
        }
        
        if (firstResponseMessage.Equals("DELETE_DIRECTORY_RESULT"))
        {
            string hashedPath = respPacket.Responses[1];
            if (Security.XxHash64.IsValidXxHash64Format(hashedPath))
            {
                if(respPacket.Status == true)
                {
                    Events.RoofDirectoryEvents.InvokeOnRoofDirectoryDeleted(hashedPath);
                }
                else
                {
                    //FAILED
                }
            }
        }

        if (firstResponseMessage.Equals("CREATE_FILE_RESULT"))
        {
            string hashedPath = respPacket.Responses[1];
            if (Security.XxHash64.IsValidXxHash64Format(hashedPath))
            {
                if(respPacket.Status == true)
                {
                    Events.RoofFileEvents.InvokeOnRoofFileCreated(hashedPath);
                }
                else
                {
                    //FAILED
                }
            }
        }
    }
    
    private static bool IsStringJson(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            using var jsonDoc = JsonDocument.Parse(source);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}