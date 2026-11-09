using Roof.Common.Networking.RoofData;

namespace Roof.NativeAppSDK.Events
{
    public class RoofFileEvents
    {
        public delegate void RoofFileEventDelegate(string? fileHashedPath, RoofFileData? fileData);
        public delegate void RoofFileEventFailedDelegate(string? hashedPath, string reason);
        
        /// <summary>
        /// Fired whenever a file has been acquired upon request.
        /// </summary>
        public static event RoofFileEventDelegate? OnRoofFileAcquired;
        
        /// <summary>
        /// Fired whenever failed to acquire a file upon request. Only limited to system exceptions from the Roof or parsing exception.
        /// </summary>
        public static event RoofFileEventFailedDelegate? OnRoofFileAcquireFailed;
        
        /// <summary>
        /// Fired whenever a file has been created on the Roof upon request.
        /// </summary>
        public static event RoofFileEventDelegate? OnRoofFileCreated;
        
        internal static void InvokeOnRoofFileAcquired(string fileHashedPath,
            RoofFileData fileData)
        {
            try
            {
                if (OnRoofFileAcquired != null)
                {
                    OnRoofFileAcquired.Invoke(fileHashedPath, fileData);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofFileAcquireFailed(string reason)
        {
            try
            {
                if (OnRoofFileAcquireFailed != null)
                {
                    OnRoofFileAcquireFailed.Invoke(null, reason);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofFileCreated(string fileHashedPath)
        {
            try
            {
                if (OnRoofFileCreated != null)
                {
                    OnRoofFileCreated.Invoke(fileHashedPath, null);
                }
            }
            catch (Exception e) { }
        }
    }
}