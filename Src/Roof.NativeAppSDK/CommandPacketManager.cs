using Roof.Common.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Roof.NativeAppSDK
{
    internal class CommandPacketManager
    {
        private static readonly RoofNetworkNativeRequestPacket _loggedInStatusCheckPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "USER_LOGIN_STATUS" }
        };

        private static readonly RoofNetworkNativeRequestPacket _getUserBaseDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "GET_USER_BASE_DIRECTORY" }
        };

        private static RoofNetworkNativeRequestPacket _getDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "GET_DIRECTORY", "hashedPath" }
        };

        private static RoofNetworkNativeRequestPacket _createDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "CREATE_DIRECTORY", "serializedDirectoryData" }
        };

        private static RoofNetworkNativeRequestPacket _updateDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "UPDATE_DIRECTORY", "updatingDirectoryHashedPath", "serializedDirectoryData" }
        };

        private static RoofNetworkNativeRequestPacket _copyDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "COPY_DIRECTORY", "copyingDirectoryHashedPath", "targetDirectoryHashedPath" }
        };

        private static RoofNetworkNativeRequestPacket _deleteDirectoryPacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "DELETE_DIRECTORY", "hashedPath" }
        };

        private static RoofNetworkNativeRequestPacket _createFilePacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "CREATE_FILE", "serializedFileData" }
        };

        private static RoofNetworkNativeRequestPacket _updateFilePacket = new RoofNetworkNativeRequestPacket()
        {
            Requests = new List<string>() { "UPDATE_FILE", "hashedPath", "serializedFileData" }
        };

        private static readonly string _serializedLoggedInStatusCheckPacket = JsonSerializer.Serialize(_loggedInStatusCheckPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        internal static byte[] SerializedLoggedInStatusCheckPacketBuffer = Encoding.UTF8.GetBytes(_serializedLoggedInStatusCheckPacket);
        
        private static readonly string _serializedGetUserBaseDirectoryPacket = JsonSerializer.Serialize(_getUserBaseDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        internal static byte[] SerializedGetUserBaseDirectoryPacketBuffer = Encoding.UTF8.GetBytes(_serializedGetUserBaseDirectoryPacket);
        
        private static string _serializedGetDirectoryPacket => JsonSerializer.Serialize(_getDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedGetDirectoryPacketBuffer => Encoding.UTF8.GetBytes(_serializedGetDirectoryPacket);

        private static string _serializedCreateDirectoryPacket => JsonSerializer.Serialize(_createDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedCreateDirectoryPacketBuffer => Encoding.UTF8.GetBytes(_serializedCreateDirectoryPacket);

        private static string _serializedUpdateDirectoryPacket => JsonSerializer.Serialize(_updateDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedUpdateDirectoryPacketBuffer => Encoding.UTF8.GetBytes(_serializedUpdateDirectoryPacket);

        private static string _serializedCopyDirectoryPacket => JsonSerializer.Serialize(_copyDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedCopyDirectoryPacketBuffer => Encoding.UTF8.GetBytes(_serializedCopyDirectoryPacket);

        private static string _serializedDeleteDirectoryPacket => JsonSerializer.Serialize(_deleteDirectoryPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedDeleteDirectoryPacketBuffer => Encoding.UTF8.GetBytes(_serializedDeleteDirectoryPacket);

        private static string _serializedCreateFilePacket => JsonSerializer.Serialize(_createFilePacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedCreateFilePacketBuffer => Encoding.UTF8.GetBytes(_serializedCreateFilePacket);

        private static string _serializedUpdateFilePacket => JsonSerializer.Serialize(_updateFilePacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
        private static byte[] _serializedUpdateFilePacketBuffer => Encoding.UTF8.GetBytes(_serializedUpdateFilePacket);

        internal static byte[] ConstructGetDirectoryPacketBuffer(string hashedPath)
        {
            if (_getDirectoryPacket.Requests == null)
            {
                _getDirectoryPacket.Requests = new List<string>() { "GET_DIRECTORY", hashedPath };
                return _serializedGetDirectoryPacketBuffer;
            }

            _getDirectoryPacket.Requests[1] = hashedPath;
            return _serializedGetDirectoryPacketBuffer;
        }

        internal static byte[] ConstructCreateDirectoryPacketBuffer(string serializedDirectoryData)
        {
            if (_createDirectoryPacket.Requests == null)
            {
                _createDirectoryPacket.Requests = new List<string>() { "CREATE_DIRECTORY", serializedDirectoryData };
                return _serializedCreateDirectoryPacketBuffer;
            }

            _createDirectoryPacket.Requests[1] = serializedDirectoryData;
            return _serializedCreateDirectoryPacketBuffer;
        }

        internal static byte[] ConstructUpdateDirectoryPacketBuffer(string hashedPath, string serializedDirectoryData)
        {
            if (_updateDirectoryPacket.Requests == null)
            {
                _updateDirectoryPacket.Requests = new List<string>() { "UPDATE_DIRECTORY", hashedPath, serializedDirectoryData };
                return _serializedUpdateDirectoryPacketBuffer;
            }

            _updateDirectoryPacket.Requests[1] = hashedPath;
            _updateDirectoryPacket.Requests[2] = serializedDirectoryData;
            return _serializedUpdateDirectoryPacketBuffer;
        }

        internal static byte[] ConstructCopyDirectoryPacketBuffer(string copyingHashedPath, string targetHashedPath)
        {
            if (_copyDirectoryPacket.Requests == null)
            {
                _copyDirectoryPacket.Requests = new List<string>() { "COPY_DIRECTORY", copyingHashedPath, targetHashedPath };
                return _serializedCopyDirectoryPacketBuffer;
            }

            _copyDirectoryPacket.Requests[1] = copyingHashedPath;
            _copyDirectoryPacket.Requests[2] = targetHashedPath;
            return _serializedCopyDirectoryPacketBuffer;
        }

        internal static byte[] ConstructDeleteDirectoryPacketBuffer(string hashedPath)
        {
            if (_deleteDirectoryPacket.Requests == null)
            {
                _deleteDirectoryPacket.Requests = new List<string>() { "DELETE_DIRECTORY", hashedPath };
                return _serializedDeleteDirectoryPacketBuffer;
            }

            _deleteDirectoryPacket.Requests[1] = hashedPath;
            return _serializedDeleteDirectoryPacketBuffer;
        }

        internal static byte[] ConstructCreateFilePacketBuffer(string serializedFileData)
        {
            if (_createFilePacket.Requests == null)
            {
                _createFilePacket.Requests = new List<string>() { "CREATE_FILE", serializedFileData };
                return _serializedCreateFilePacketBuffer;
            }

            _createFilePacket.Requests[1] = serializedFileData;
            return _serializedCreateFilePacketBuffer;
        }

        internal static byte[] ConstructUpdateFilePacketBuffer(string hashedPath, string serializedFileData)
        {
            if (_updateFilePacket.Requests == null)
            {
                _updateFilePacket.Requests = new List<string>() { "UPDATE_FILE", hashedPath, serializedFileData };
                return _serializedUpdateFilePacketBuffer;
            }

            _updateFilePacket.Requests[1] = hashedPath;
            _updateFilePacket.Requests[2] = serializedFileData;
            return _serializedUpdateFilePacketBuffer;
        }
    }
}
