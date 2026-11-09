using Roof.Common.Networking.RoofData;

namespace Roof.NativeAppSDK.Events
{
    public class RoofDirectoryEvents
    {
        public delegate void RoofDirectoryEventDelegate(string? directoryHashedPath, RoofDirectoryData? directoryData);
        public delegate void RoofDirectoryEventFailedDelegate(string? hashedPath, string reason);
        
        /// <summary>
        /// Fired whenever base directory has been acquired upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofBaseDirectoryAcquired;
        
        /// <summary>
        /// Fired whenever failed to acquire base directory upon request. Only limited to system exceptions from the Roof or parsing exception.
        /// </summary>
        public static event RoofDirectoryEventFailedDelegate? OnRoofBaseDirectoryAcquireFailed;
        
        /// <summary>
        /// Fired whenever a directory has been acquired upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofDirectoryAcquired;
        
        /// <summary>
        /// Fired whenever failed to acquire a directory upon request. Only limited to system exceptions from the Roof or parsing exception.
        /// </summary>
        public static event RoofDirectoryEventFailedDelegate? OnRoofDirectoryAcquireFailed;
        
        /// <summary>
        /// Fired whenever a directory has been created on the Roof upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofDirectoryCreated;

        /// <summary>
        /// Fired whenever a directory has been updated on the Roof upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofDirectoryUpdated;
        
        /// <summary>
        /// Fired whenever failed to update a directory upon request. Only limited to system exceptions from the Roof or parsing exception.
        /// </summary>
        public static event RoofDirectoryEventFailedDelegate? OnRoofDirectoryUpdateFailed;

        /// <summary>
        /// Fired whenever a directory has been copied to the target directory on the Roof upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofDirectoryCopied;
        
        /// <summary>
        /// Fired whenever failed to copy a directory to the target directory upon request. Only limited to system exceptions from the Roof or parsing exception.
        /// </summary>
        public static event RoofDirectoryEventFailedDelegate? OnRoofDirectoryCopyFailed;
        
        /// <summary>
        /// Fired whenever a directory has been deleted on the Roof upon request.
        /// </summary>
        public static event RoofDirectoryEventDelegate? OnRoofDirectoryDeleted;

        internal static void InvokeOnRoofBaseDirectoryAcquired(string directoryHashedPath,
            RoofDirectoryData directoryData)
        {
            try
            {
                if (OnRoofBaseDirectoryAcquired != null)
                {
                    OnRoofBaseDirectoryAcquired.Invoke(directoryHashedPath, directoryData);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofBaseDirectoryAcquireFailed(string reason)
        {
            try
            {
                if (OnRoofBaseDirectoryAcquireFailed != null)
                {
                    OnRoofBaseDirectoryAcquireFailed.Invoke(null, reason);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofDirectoryAcquired(string directoryHashedPath, RoofDirectoryData directoryData)
        {
            try
            {
                if (OnRoofDirectoryAcquired != null)
                {
                    OnRoofDirectoryAcquired.Invoke(directoryHashedPath, directoryData);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofDirectoryAcquireFailed(string hashedPath, string reason)
        {
            try
            {
                if (OnRoofDirectoryAcquireFailed != null)
                {
                    OnRoofDirectoryAcquireFailed.Invoke(hashedPath, reason);
                }
            }
            catch (Exception e) { }
        }

        internal static void InvokeOnRoofDirectoryCreated(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryCreated != null)
                {
                    OnRoofDirectoryCreated.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofDirectoryUpdated(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryUpdated != null)
                {
                    OnRoofDirectoryUpdated.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofDirectoryUpdateFailed(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryUpdateFailed != null)
                {
                    OnRoofDirectoryUpdateFailed.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }

        internal static void InvokeOnRoofDirectoryCopied(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryCopied != null)
                {
                    OnRoofDirectoryCopied.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }
        
        internal static void InvokeOnRoofDirectoryCopyFailed(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryCopyFailed != null)
                {
                    OnRoofDirectoryCopyFailed.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }

        internal static void InvokeOnRoofDirectoryDeleted(string directoryHashedPath)
        {
            try
            {
                if (OnRoofDirectoryDeleted != null)
                {
                    OnRoofDirectoryDeleted.Invoke(directoryHashedPath, null);
                }
            }
            catch (Exception e) { }
        }
    }
}