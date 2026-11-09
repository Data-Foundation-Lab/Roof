using System.Text.Json;
using Roof.Common.Networking;
using Roof.Common.Networking.RoofData;
using Roof.Common.Tools;
using Roof.Engine.Structs;
using Roof.Engine.Tools;

namespace Roof.Engine.Managers.CommandHandlers
{
    internal class FileSystemCommandHandlers
    {
        public static async Task FileSystemHandler_GetBaseDirectory(RoofClient client, List<string> args)
        {
            try
            {
                Logger.LogToFile("FileSystemCommandHandlers] - FileSystemHandler_GetBaseDirectory(): Base directory was requested. Processing...");
                
                var respPacket = new RoofNetworkNativeResponsePacket();

                if(args.Count != 0)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetBaseDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_BASE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var baseDirData = client.FSManager.GetBaseDirectory();
                if (baseDirData == null) 
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetBaseDirectory(): Base directory unavailable.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_BASE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid base directory.");
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("GET_BASE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(JsonSerializer.Serialize(baseDirData, RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData));
                    
                    Logger.LogToFile("FileSystemCommandHandlers] - FileSystemHandler_GetBaseDirectory(): Successfully returned base directory.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch(Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetBaseDirectory(): Exception occurred while trying to handle base directory get request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_BASE_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_GetDirectory(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                if (!Security.XxHash64.IsValidXxHash64Format(args[0]))
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args[0]);
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                Logger.LogToFile($"FileSystemCommandHandlers] - FileSystemHandler_GetDirectory(): Directory [{args[0]}] was requested. Processing...");

                var dirGetResult = client.FSManager.GetDirectory(args.First());
                if (dirGetResult == null)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetDirectory(): Requested directory unavailable.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid directory.");
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("GET_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(JsonSerializer.Serialize(dirGetResult, RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData));
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetDirectory(): Exception occurred while trying to handle directory get request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(args[0]);
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_GetDirectories(RoofClient client, List<string> args)
        {
            Logger.LogToFile("FileSystemHandler_GetDirectories(): UNSUPPORTED.", LogSeverity.Danger);
            
            var respPacket = new RoofNetworkNativeResponsePacket();
            respPacket.Status = false;
            respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
            respPacket.AddResponse("Invalid operation.");
            
            await ClientManager.SendMessageToClient(client, respPacket);
            /*try
            {
                RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();
                var encryptionInfo = client.GetEncryptionInfo();

                if (args.Count != 1 || string.IsNullOrEmpty(args.First()) || !IsStringJson(args.First()))
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                List<string> deserialisedDirectoryList;

                try
                {
                    //Deserialisation attempt.
                    deserialisedDirectoryList = JsonSerializer.Deserialize(args.First(), ListOfStringJsonSerializerContext.Default.ListString);
                }
                catch (Exception ex)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetDirectories(): Failed to parse argument for deserialisation. Exception: {ex.Message}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
                    respPacket.AddResponse("Failed to deserialize list of directories.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                List<RoofDirectoryData> returningDirectoryList = new List<RoofDirectoryData>();

                foreach(var dirHashedPath in deserialisedDirectoryList)
                {
                    var dirGetResult = client.FSManager.GetDirectory(dirHashedPath);

                    if (dirGetResult != null && returningDirectoryList.Contains(dirGetResult) == false)
                        returningDirectoryList.Add(dirGetResult);
                }

                if(returningDirectoryList.Count == 0)
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
                    respPacket.AddResponse("No available directories found.");
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
                    respPacket.AddResponse(JsonSerializer.Serialize(returningDirectoryList, ListOfRoofDirectoryDataSerializerContext.Default.ListRoofDirectoryData));
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetDirectories(): Exception occurred while trying to handle directory get request. Exception: {ex.Message}", LogSeverity.Danger);

                RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_DIRECTORIES_COMMAND_RESULT");
                respPacket.AddResponse("Internal engine exception occurred while handling the operation.");

                var encryptionInfo = client.GetEncryptionInfo();
                await ClientManager.SendMessageToClient(client, respPacket);
            }*/
        }

        public static async Task FileSystemHandler_CreateDirectory(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (!args[1].IsStringJson())
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_CreateDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args[0]);
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                RoofDirectoryData? deserialisedDirectory; 
                try
                {
                    //Deserialisation attempt.
                    deserialisedDirectory = JsonSerializer.Deserialize(args[1], RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData);
                }
                catch(Exception ex)
                {
                    Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateDirectory(): Failed to parse argument for deserialisation.", ex);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args[0]);
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                bool createDirResult = await client.FSManager.CreateDirectory(deserialisedDirectory);
                if (createDirResult)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateDirectory(): Successfully created directory {deserialisedDirectory.Name} at {deserialisedDirectory.Path}");

                    respPacket.Status = true;
                    respPacket.AddResponse("CREATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(deserialisedDirectory.HashedPath);
                }
                else
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateDirectory(): failed to create directory {deserialisedDirectory.Name} at {deserialisedDirectory.Path}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args[0]);
                    respPacket.AddResponse("Failed to create physical directory.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateDirectory(): Exception occurred while trying to handle directory create request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("CREATE_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(args[0]);
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_UpdateDirectory(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (!Security.XxHash64.IsValidXxHash64Format(args.First()) || !args[1].IsStringJson())
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_UpdateDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                RoofDirectoryData? deserialisedDirectory;
                try
                {
                    //Deserialisation attempt.
                    deserialisedDirectory = JsonSerializer.Deserialize(args[1], RoofDirectoryDataJSONContext.PreserveReference.RoofDirectoryData);
                }
                catch (Exception ex)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateDirectory(): Failed to parse argument for deserialisation. Exception: {ex.Message}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                bool updateDirResult = client.FSManager.UpdateDirectory(args.First(), deserialisedDirectory);
                if (updateDirResult)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateDirectory(): Successfully updated directory {deserialisedDirectory.Name}");

                    respPacket.Status = true;
                    respPacket.AddResponse("UPDATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                }
                else
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateDirectory(): failed to update directory {deserialisedDirectory.Name}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Failed to find a local directory that matches the requested directory.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateDirectory(): Exception occurred while trying to handle directory update request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("UPDATE_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task FileSystemHandler_CopyDirectory(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (!Security.XxHash64.IsValidXxHash64Format(args.First()) || !Security.XxHash64.IsValidXxHash64Format(args[1]))
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_CopyDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("COPY_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                bool copyDirResult = client.FSManager.CopyDirectory(args.First(), args[1]);
                if (copyDirResult)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CopyDirectory(): Successfully copied directory {args.First()} to {args[1]}");

                    respPacket.Status = true;
                    respPacket.AddResponse("COPY_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                }
                else
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CopyDirectory(): failed to copy directory {args.First()} to {args[1]}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("COPY_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Failed to find a local directory that matches the requested directory.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CopyDirectory(): Exception occurred while trying to handle directory copy request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("COPY_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_DeleteDirectory(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (Security.XxHash64.IsValidXxHash64Format(args.First()) == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_DeleteDirectory(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("DELETE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var dirDeleteResult = client.FSManager.DeleteDirectory(args.First());
                if (dirDeleteResult.Item1 == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_DeleteDirectory(): Directory deletion failed.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("DELETE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse(dirDeleteResult.Item2);
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("DELETE_DIRECTORY_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_DeleteDirectory(): Exception occurred while trying to handle directory delete request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("DELETE_DIRECTORY_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_GetFile(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (Security.XxHash64.IsValidXxHash64Format(args.First()) == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetFile(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var fileGetResult = await client.FSManager.GetFile(args.First());
                if (fileGetResult == null)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetFile(): Requested file unavailable.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Requested file does not exist.");
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("GET_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(JsonSerializer.Serialize(fileGetResult, RoofFileDataJSONContext.PreserveReference.RoofFileData));
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetFile(): Exception occurred while trying to handle file get request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_FILE_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_CreateFile(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (!args[1].IsStringJson())
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_CreateFile(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                RoofFileData? deserialisedFile;
                try
                {
                    //Deserialisation attempt.
                    deserialisedFile = JsonSerializer.Deserialize(args[1], RoofFileDataJSONContext.PreserveReference.RoofFileData);
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateFile(): Failed to parse argument for deserialisation.", ex);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                bool createFileResult = await client.FSManager.CreateFile(deserialisedFile);
                if (createFileResult)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateFile(): Successfully created file {deserialisedFile.Name} at {deserialisedFile.Path}");

                    respPacket.Status = true;
                    respPacket.AddResponse("CREATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(deserialisedFile.HashedPath);
                }
                else
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateFile(): failed to create file {deserialisedFile.Name} at {deserialisedFile.Path}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("CREATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(deserialisedFile.HashedPath);
                    respPacket.AddResponse("Failed to create physical file.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_CreateFile(): Exception occurred while trying to handle file create request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("CREATE_FILE_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_UpdateFile(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (!args[1].IsStringJson())
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_UpdateFile(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                RoofFileData? deserialisedFile;
                try
                {
                    //Deserialisation attempt.
                    deserialisedFile = JsonSerializer.Deserialize(args[1], RoofFileDataJSONContext.PreserveReference.RoofFileData)
                        ?? throw new JsonException("File update payload cannot be null.");
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateFile(): Failed to parse argument for deserialisation.", ex);

                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                // The command key identifies the existing file; the payload may contain its new path.
                deserialisedFile.SetHashedPath(args[0]);
                var updateFileResult = await client.FSManager.UpdateFile(deserialisedFile);
                if (updateFileResult.Item1)
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateFile(): Successfully updated file {deserialisedFile.Name}");
                    
                    respPacket.Status = true;
                    respPacket.AddResponse("UPDATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(deserialisedFile.HashedPath);
                    respPacket.AddResponse(JsonSerializer.Serialize(updateFileResult.Item2, RoofFileDataJSONContext.PreserveReference.RoofFileData));
                }
                else
                {
                    Logger.LogToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateFile(): failed to update file {deserialisedFile.Name}", LogSeverity.Danger);

                    respPacket.Status = false;
                    respPacket.AddResponse("UPDATE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Failed to find a local file that matches the requested file.");
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_UpdateFile(): Exception occurred while trying to handle file update request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("UPDATE_FILE_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }

        public static async Task FileSystemHandler_DeleteFile(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();
                
                if (Security.XxHash64.IsValidXxHash64Format(args.First()) == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_DeleteFile(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("DELETE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var fileDeleteResult = await client.FSManager.DeleteFile(args.First());
                if (fileDeleteResult.Item1 == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_DeleteFile(): File deletion failed.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("DELETE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse(fileDeleteResult.Item2);
                }
                else
                {
                    respPacket.Status = true;
                    respPacket.AddResponse("DELETE_FILE_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                }

                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_DeleteFile(): Exception occurred while trying to handle file delete request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("DELETE_FILE_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
        
        public static async Task FileSystemHandler_GetFileData(RoofClient client, List<string> args)
        {
            try
            {
                var respPacket = new RoofNetworkNativeResponsePacket();

                if (Security.XxHash64.IsValidXxHash64Format(args.First()) == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetFileData(): Request rejected because arguments were invalid.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_FILE_DATA_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse("Invalid argument.");

                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }

                var fileGetDataResult = await client.FSManager.GetFileData(args.First());
                if (fileGetDataResult.Item1 == false)
                {
                    Logger.LogToFile("[FileSystemCommandHandlers] - FileSystemHandler_GetFileData(): Reading file data failed.", LogSeverity.Warning);
                    respPacket.Status = false;
                    respPacket.AddResponse("GET_FILE_DATA_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());
                    respPacket.AddResponse(fileGetDataResult.Item2);
                    
                    await ClientManager.SendMessageToClient(client, respPacket);
                    return;
                }
                /*
                var totalLength = fileGetDataResult.Item2.Length;
                var dividedData = (int)Math.Ceiling((double)totalLength / DEFAULT_FILE_CHUNK_SIZE);

                for (int i = 0; i < dividedData; i++)
                {
                    var startIndex = i * DEFAULT_FILE_CHUNK_SIZE;
                    var length = Math.Min(DEFAULT_FILE_CHUNK_SIZE, totalLength - startIndex);

                    respPacket.Status = true;
                    respPacket.AddResponse("GET_FILE_DATA_COMMAND_RESULT");
                    respPacket.AddResponse(args.First());

                    var dataChunk = fileGetDataResult.Item2.Substring(startIndex, length);
                    respPacket.AddResponse(dataChunk);

                    await ClientManager.SendMessageToClient(client, respPacket);
                }*/
                
                respPacket.Status = true;
                respPacket.AddResponse("GET_FILE_DATA_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(fileGetDataResult.Item2);
                
                await ClientManager.SendMessageToClient(client, respPacket);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[FileSystemCommandHandlers] - FileSystemHandler_GetFileData(): Exception occurred while trying to handle file get data request.", ex);

                var respPacket = new RoofNetworkNativeResponsePacket();
                respPacket.Status = false;
                respPacket.AddResponse("GET_FILE_DATA_COMMAND_RESULT");
                respPacket.AddResponse(args.First());
                respPacket.AddResponse(ex.Message);

                await ClientManager.SendMessageToClient(client, respPacket);
            }
        }
    }
}