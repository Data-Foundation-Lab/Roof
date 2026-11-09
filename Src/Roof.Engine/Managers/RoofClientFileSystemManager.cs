using Roof.Common.Networking;
using Roof.Common.Networking.RoofData;
using Roof.Common.Tools;
using Roof.Engine.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;
using File = System.IO.File;

namespace Roof.Engine.Managers
{
    /// <summary>
    /// Filesystem manager class created for each client.
    /// </summary>
    public class ClientFileSystemManager
    {
        // Protects the complete physical operation, all four caches, and their mutable metadata.
        // Core helpers require this gate and must never call a public method (the gate is not reentrant).
        private static readonly SemaphoreSlim _fileSystemGate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _sharedPlaygroundSetupGate = new SemaphoreSlim(1, 1);
        private static RoofDirectoryData? _baseDirectory;
        private static readonly Dictionary<string, RoofDirectoryData?> _loadedTopLevelDirectories = new Dictionary<string, RoofDirectoryData?>();
        private static readonly Dictionary<string, RoofDirectoryData?> _loadedSubDirectories = new Dictionary<string, RoofDirectoryData?>();
        private static readonly Dictionary<string, RoofFileData?> _loadedTopLevelFiles = new Dictionary<string, RoofFileData?>();
        private static readonly Dictionary<string, RoofFileData?> _loadedSubFiles = new Dictionary<string, RoofFileData?>();

        public const string PlaygroundPath = "/srv/roof/playground";

        private const string SftpGroupSetupCommand =
            "getent group roofsftpusers >/dev/null; status=$?; " +
            "case $status in 0) ;; 2) sudo groupadd -f roofsftpusers ;; *) exit \"$status\" ;; esac";

        // Keep successful package-manager chatter out of Shell's string-based error contract,
        // but preserve diagnostics and the exit status on failure.
        private const string InstallAclCommand =
            "output=$(sudo apt-get update 2>&1 && sudo apt-get install -y acl 2>&1)\n" +
            "status=$?\n" +
            "if [ \"$status\" -ne 0 ]; then\n" +
            "    printf 'ACL dependency installation failed (exit %s):\\n%s\\n' \"$status\" \"$output\" >&2\n" +
            "    exit \"$status\"\n" +
            "fi\n" +
            "command -v setfacl >/dev/null";

        private readonly string _basePath;

        private readonly string _currentWorkingDirectory;
        public string CurrentWorkingDirectory => _currentWorkingDirectory;

        public ClientFileSystemManager(string clientUsername)
        {
            if (!SecurityManager.IsValidUsername(clientUsername))
                throw new ArgumentException("Invalid Linux username.");

            _basePath = PlaygroundPath;

            _currentWorkingDirectory = _basePath;

            try
            {
                WithFileSystemLock(() => { if (_baseDirectory == null) LoadBaseDirectory(); return true; });
            }
            catch (Exception ex) 
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager]: Failed to access directory info for client's base directory.", ex);
                throw;
            }
        }

        public static async Task EnsureSharedPlayground()
        {
            await _sharedPlaygroundSetupGate.WaitAsync();
            try
            {
                await EnsureSharedPlaygroundCore();
            }
            finally
            {
                _sharedPlaygroundSetupGate.Release();
            }
        }

        private static async Task EnsureSharedPlaygroundCore()
        {
            // getent returns 2 for a missing group; other failures must not be hidden.
            // Handle that status in the shell before ExecuteAsync turns it into an error string.
            string groupResult = await Shell.ExecuteAsync(SftpGroupSetupCommand);
            if (!string.IsNullOrEmpty(groupResult))
                throw new IOException("Failed to ensure SFTP group: " + groupResult);

            string aclResult = await Shell.ExecuteAsync("command -v setfacl >/dev/null 2>&1");
            if (!string.IsNullOrEmpty(aclResult))
            {
                Logger.LogToFile("[RoofClientFileSystemManager]: Installing ACL support for the shared playground...");
                aclResult = await Shell.ExecuteAsync(InstallAclCommand, 120);
                if (!string.IsNullOrEmpty(aclResult))
                    throw new IOException("Failed to install ACL support: " + aclResult);
            }

            // Default ACLs also cover files uploaded through SFTP with a restrictive umask.
            string result = await Shell.ExecuteAsync("sudo mkdir -p /srv/roof/playground && " +
                "sudo chown root:root /srv/roof && sudo chmod 755 /srv/roof && " +
                "sudo chgrp -R roofsftpusers /srv/roof/playground && " +
                "sudo chmod -R g+rwX,o-rwx /srv/roof/playground && " +
                "sudo find /srv/roof/playground -type d -exec chmod g+s {} + && " +
                "sudo find /srv/roof/playground -type d -exec setfacl -m d:u::rwx,d:g::rwx,d:m::rwx,d:o::--- {} +");
            if (!string.IsNullOrEmpty(result))
                throw new IOException("Failed to configure shared playground: " + result);

            const string sshdConfigFile = "/etc/ssh/sshd_config";
            var lines = await File.ReadAllLinesAsync(sshdConfigFile);
            bool roofGroup = false;
            bool foundRoofGroup = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("Match ", StringComparison.OrdinalIgnoreCase))
                    roofGroup = lines[i].Trim().Equals("Match Group roofsftpusers", StringComparison.OrdinalIgnoreCase);
                if (!roofGroup)
                    continue;
                foundRoofGroup = true;
                if (lines[i].TrimStart().StartsWith("ChrootDirectory ", StringComparison.OrdinalIgnoreCase))
                    lines[i] = "    ChrootDirectory /srv/roof";
                if (lines[i].TrimStart().StartsWith("ForceCommand internal-sftp", StringComparison.OrdinalIgnoreCase))
                    lines[i] = "    ForceCommand internal-sftp -d /playground -u 0007";
            }
            // Group creation may have succeeded during an earlier, interrupted setup.
            // Ensure the Match block independently of whether the group already existed.
            if (!foundRoofGroup)
            {
                lines = lines.Concat(new[]
                {
                    "",
                    "# Restrict Roof clients to the shared SFTP playground",
                    "Match Group roofsftpusers",
                    "    ChrootDirectory /srv/roof",
                    "    ForceCommand internal-sftp -d /playground -u 0007",
                    "    X11Forwarding no",
                    "    AllowTcpForwarding no"
                }).ToArray();
            }
            await File.WriteAllLinesAsync(sshdConfigFile, lines);
        }

        // TEMPORARY MIGRATION: Remove this method, its helper, and the startup call after
        // all existing per-user playground migrations are completed. Keep ACL installation
        // in EnsureSharedPlayground; new installations still require setfacl.
        public static void MigrateLegacyPlaygrounds()
        {
            var targetPath = SafePath.CanonicalizeUnder(PlaygroundPath, "/srv/roof");
            Directory.CreateDirectory(targetPath);
            var usernames = ConfigurationManager.GetConfiguration().RegisteredClients.Values
                .Select(client => client.OSSystemUsername).Where(username => !string.IsNullOrEmpty(username)).Distinct();
            foreach (var username in usernames)
            {
                if (!SecurityManager.IsValidUsername(username))
                    throw new IOException("Invalid username in playground migration.");

                var sourcePath = SafePath.CanonicalizeUnder($"/home/{username}/playground", $"/home/{username}");
                if (!Directory.Exists(sourcePath))
                    continue;

                Logger.LogToFile($"[RoofClientFileSystemManager]: Migrating {sourcePath} to {targetPath}...");
                MigratePlaygroundDirectory(sourcePath, targetPath, sourcePath, targetPath, username);
                Logger.LogToFile($"[RoofClientFileSystemManager]: Playground migration completed for {username}.");
            }
        }

        private static void MigratePlaygroundDirectory(string sourcePath, string targetPath, string sourceRoot, string targetRoot, string username)
        {
            Directory.CreateDirectory(SafePath.CanonicalizeUnder(targetPath, targetRoot));
            foreach (var entry in Directory.GetFileSystemEntries(sourcePath))
            {
                var source = SafePath.CanonicalizeUnder(entry, sourceRoot);
                var target = SafePath.CanonicalizeUnder(Path.Combine(targetPath, Path.GetFileName(entry)), targetRoot);
                bool isDirectory = Directory.Exists(source);
                if (File.Exists(target) || (!isDirectory && Directory.Exists(target)))
                {
                    // Preserve both versions on collisions; never overwrite shared or migrated data.
                    string conflictPath = target + ".migrated-" + username;
                    target = SafePath.CanonicalizeUnder(conflictPath, targetRoot);
                    int suffix = 1;
                    while (File.Exists(target) || Directory.Exists(target))
                        target = SafePath.CanonicalizeUnder(conflictPath + "-" + suffix++, targetRoot);
                    Logger.LogToFile($"[RoofClientFileSystemManager]: Migration conflict for {source}. Preserving it as {target}.", LogSeverity.Warning);
                }

                if (isDirectory)
                    MigratePlaygroundDirectory(source, target, sourceRoot, targetRoot, username);
                else
                    File.Move(source, target);
            }
            // Only remove an empty source directory. An interrupted migration can be retried.
            Directory.Delete(sourcePath);
        }

        // Public reads return detached snapshots: response serialization happens after the gate is released.
        public RoofDirectoryData? GetBaseDirectory() =>
            WithFileSystemLock(() => SnapshotDirectory(_baseDirectory == null ? null : GetDirectoryCore(_baseDirectory.HashedPath)));

        public RoofDirectoryData? GetDirectory(string hashedPath) =>
            WithFileSystemLock(() => SnapshotDirectory(GetDirectoryCore(hashedPath)));

        public Task<RoofFileData?> GetFile(string hashedPath) =>
            WithFileSystemLockAsync(() => Task.FromResult(SnapshotFile(GetFileCore(hashedPath))));

        public Task<bool> CreateDirectory(RoofDirectoryData dirData)
        {
            var request = SnapshotDirectory(dirData)!;
            return WithFileSystemLockAsync(() => CreateDirectoryCore(request));
        }

        public bool UpdateDirectory(string updatingDirHashedPath, RoofDirectoryData updatedDirData)
        {
            var request = SnapshotDirectory(updatedDirData)!;
            return WithFileSystemLock(() => UpdateDirectoryCore(updatingDirHashedPath, request));
        }

        public bool CopyDirectory(string copyingDirectoryHashedPath, string targetDirectoryHashedPath) =>
            WithFileSystemLock(() => CopyDirectoryCore(copyingDirectoryHashedPath, targetDirectoryHashedPath));

        public (bool, string) DeleteDirectory(string hashedPath) =>
            WithFileSystemLock(() => DeleteDirectoryCore(hashedPath));

        public Task<bool> CreateFile(RoofFileData fileData)
        {
            var request = SnapshotFile(fileData)!;
            return WithFileSystemLockAsync(() => CreateFileCore(request));
        }

        public Task<(bool, RoofFileData?)> UpdateFile(RoofFileData fileData)
        {
            var request = SnapshotFile(fileData)!;
            return WithFileSystemLockAsync(async () =>
            {
                var result = await UpdateFileCore(request).ConfigureAwait(false);
                return (result.Item1, SnapshotFile(result.Item2));
            });
        }

        public Task<(bool, string)> DeleteFile(string hashedPath) =>
            WithFileSystemLockAsync(() => Task.FromResult(DeleteFileCore(hashedPath)));

        public Task<(bool, string)> GetFileData(string fileHashedPath) =>
            WithFileSystemLockAsync(() => GetFileDataCore(fileHashedPath));

        private T WithFileSystemLock<T>(Func<T> operation)
        {
            _fileSystemGate.Wait();
            try { return operation(); }
            finally { _fileSystemGate.Release(); }
        }

        private async Task<T> WithFileSystemLockAsync<T>(Func<Task<T>> operation)
        {
            await _fileSystemGate.WaitAsync().ConfigureAwait(false);
            try { return await operation().ConfigureAwait(false); }
            finally { _fileSystemGate.Release(); }
        }

        private static RoofDirectoryData? SnapshotDirectory(RoofDirectoryData? data)
        {
            if (data == null)
                return null;
            var snapshot = new RoofDirectoryData
            {
                Name = data.Name, Path = data.Path, ParentDirectory = data.ParentDirectory,
                Size = data.Size, LastModifiedUTC = data.LastModifiedUTC,
                Files = data.Files == null ? null : new HashSet<string>(data.Files),
                SubDirectories = data.SubDirectories == null ? null : new HashSet<string>(data.SubDirectories)
            };
            snapshot.SetHashedPath(data.HashedPath);
            return snapshot;
        }

        private static RoofFileData? SnapshotFile(RoofFileData? data)
        {
            if (data == null)
                return null;
            var snapshot = new RoofFileData
            {
                Name = data.Name, Path = data.Path, ParentDirectory = data.ParentDirectory,
                Size = data.Size, FileHash = data.FileHash, LastModifiedUTC = data.LastModifiedUTC,
                Data = data.Data?.ToArray(), ShouldOverwriteData = data.ShouldOverwriteData,
                ShouldAppendData = data.ShouldAppendData
            };
            snapshot.SetHashedPath(data.HashedPath);
            return snapshot;
        }

        private string ValidatePath(string path, string hashedPath)
        {
            var safePath = SafePath.CanonicalizeUnder(path, _basePath);
            if (hashedPath != Security.XxHash64.GenerateHashedPath(safePath))
                throw new ArgumentException("Hashed path does not match the physical path.");

            return safePath;
        }

        private void ValidateTree(string path)
        {
            path = SafePath.CanonicalizeUnder(path, _basePath);
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                var safeChild = SafePath.CanonicalizeUnder(child, _basePath);
                if (Directory.Exists(safeChild))
                    ValidateTree(safeChild);
            }
        }

        private void LoadBaseDirectory()
        {
            DirectoryInfo dirInfo = new DirectoryInfo(SafePath.CanonicalizeUnder(_basePath, _basePath));

            // Create the base directory data.
            var baseDirectory = new RoofDirectoryData()
            {
                Name = "Home",
                Path = _basePath,
                Size = (int)GetDirectorySize(dirInfo)
            };

            // Load top-level directories.
            DirectoryInfo[] topLevelDirs = dirInfo.GetDirectories();
            if (topLevelDirs.Any())
            {
                baseDirectory.SubDirectories = new HashSet<string>();

                foreach (var tlDir in topLevelDirs)
                {
                    SafePath.CanonicalizeUnder(tlDir.FullName, _basePath);
                    var rfDir = new RoofDirectoryData()
                    {
                        Name = tlDir.Name,
                        Path = tlDir.FullName,
                        ParentDirectory = baseDirectory.HashedPath,
                        Size = (int)GetDirectorySize(tlDir)
                    };
                    
                    FileInfo[] topLevelDirFiles = tlDir.GetFiles("*", SearchOption.TopDirectoryOnly);
                    if (topLevelDirFiles.Any())
                    {
                        rfDir.Files = new HashSet<string>();

                        foreach (var tlFile in topLevelDirFiles)
                        {
                            SafePath.CanonicalizeUnder(tlFile.FullName, _basePath);
                            var rfFile = new RoofFileData()
                            {
                                Name = tlFile.Name,
                                Path = tlFile.FullName,
                                ParentDirectory = rfDir.HashedPath,
                                Size = (int)tlFile.Length
                            };
                            
                            rfDir.Files.Add(rfFile.HashedPath);
                            _loadedSubFiles.Add(rfFile.HashedPath, rfFile);
                        }
                    }

                    baseDirectory.SubDirectories.Add(rfDir.HashedPath);
                    _loadedTopLevelDirectories.Add(rfDir.HashedPath, rfDir);
                }
            }

            // Load top-level file.
            FileInfo[] topLevelFiles = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
            if (topLevelFiles.Any())
            {
                baseDirectory.Files = new HashSet<string>();

                foreach (var tlFile in topLevelFiles)
                {
                    SafePath.CanonicalizeUnder(tlFile.FullName, _basePath);
                    var rfFile = new RoofFileData()
                    {
                        Name = tlFile.Name,
                        Path = tlFile.FullName,
                        ParentDirectory = baseDirectory.HashedPath,
                        Size = (int)tlFile.Length
                    };

                    baseDirectory.Files.Add(rfFile.HashedPath);
                    _loadedTopLevelFiles.Add(rfFile.HashedPath, rfFile);
                }
            }

            // Use BFS to traverse and load all subdirectories starting from top-level directories.
            Queue<RoofDirectoryData> queue = new Queue<RoofDirectoryData>();

            // Enqueue all top-level directories.
            foreach (var tlDir in _loadedTopLevelDirectories.Values)
            {
                if (tlDir != null)
                {
                    queue.Enqueue(tlDir);
                }
            }

            while (queue.Count > 0)
            {
                var currentDir = queue.Dequeue();
                DirectoryInfo currentDirInfo = new DirectoryInfo(SafePath.CanonicalizeUnder(currentDir.Path, _basePath));

                DirectoryInfo[] subDirs = null;
                try
                {
                    // Get subdirectories for the current directory.
                    subDirs = currentDirInfo.GetDirectories();
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile($"[RoofClientFileSystemManager]: Failed to access subdirectories of {currentDir.Path}.", ex);
                    continue;
                }

                if (subDirs.Any())
                {
                    // Initialize the subdirectory collection if needed.
                    if (currentDir.SubDirectories == null)
                    {
                        currentDir.SubDirectories = new HashSet<string>();
                    }

                    foreach (var subDir in subDirs)
                    {
                        SafePath.CanonicalizeUnder(subDir.FullName, _basePath);
                        var subDirData = new RoofDirectoryData()
                        {
                            Name = subDir.Name,
                            Path = subDir.FullName,
                            ParentDirectory = currentDir.HashedPath,
                            Size = (int)GetDirectorySize(subDir)
                        };

                        FileInfo[] subDirFiles = subDir.GetFiles("*", SearchOption.TopDirectoryOnly);
                        if (subDirFiles.Any())
                        {
                            subDirData.Files = new HashSet<string>();

                            foreach (var file in subDirFiles)
                            {
                                SafePath.CanonicalizeUnder(file.FullName, _basePath);
                                var rfFile = new RoofFileData()
                                {
                                    Name = file.Name,
                                    Path = file.FullName,
                                    ParentDirectory = subDirData.HashedPath,
                                    Size = (int)file.Length
                                };

                                subDirData.Files.Add(rfFile.HashedPath);
                                _loadedSubFiles.Add(rfFile.HashedPath, rfFile);
                            }
                        }

                        // Record the subdirectory in the parent's collection.
                        currentDir.SubDirectories.Add(subDirData.HashedPath);

                        // Add to the global subdirectories dictionary if not already present.
                        if (!_loadedSubDirectories.ContainsKey(subDirData.HashedPath))
                        {
                            _loadedSubDirectories.Add(subDirData.HashedPath, subDirData);
                        }

                        // Enqueue this subdirectory for further traversal.
                        queue.Enqueue(subDirData);
                    }
                }
            }

            // Set the base directory.
            _baseDirectory = baseDirectory;
        }

        private RoofDirectoryData? GetDirectoryCore(string hashedPath)
        {
            if (_baseDirectory == null || string.IsNullOrEmpty(hashedPath))
                return null;

            //Check if the directory is base directory
            if (hashedPath == _baseDirectory.HashedPath)
            {
                ValidatePath(_baseDirectory.Path, hashedPath);
                _baseDirectory.LastModifiedUTC = Directory.GetLastWriteTimeUtc(_baseDirectory.Path);
                return _baseDirectory;
            }

            // Check if the directory exists in the top-level dictionary.
            if (_loadedTopLevelDirectories.TryGetValue(hashedPath, out var directory) && directory != null)
            {
                ValidatePath(directory.Path, hashedPath);
                directory.LastModifiedUTC = Directory.GetLastWriteTimeUtc(directory.Path);
                return directory;
            }

            if(_loadedSubDirectories.TryGetValue(hashedPath, out var subDir) && subDir != null)
            {
                ValidatePath(subDir.Path, hashedPath);
                subDir.LastModifiedUTC = Directory.GetLastWriteTimeUtc(subDir.Path);
                return subDir;
            }

            // If the directory was not found, return null.
            return null;
        }

        private async Task<bool> CreateDirectoryCore(RoofDirectoryData dirData)
        {
            try
            {
                var safePath = ValidatePath(dirData.Path, dirData.HashedPath);
                var parent = GetDirectoryCore(dirData.ParentDirectory);
                if (parent == null || !SecurityManager.IsValidFilename(dirData.Name) ||
                    Path.GetFileName(safePath) != dirData.Name || Path.GetDirectoryName(safePath) != parent.Path ||
                    Directory.Exists(safePath) || File.Exists(safePath))
                    return false;

                dirData.Path = safePath;
                dirData.Files = null;
                dirData.SubDirectories = null;
                
                if (Directory.CreateDirectory(safePath) != null)
                {
                    var quotedPath = SafePath.ShellQuote(safePath);
                    
                    string chownUserDirOwnRoot = await Shell.ExecuteAsync($"sudo chown root:roofsftpusers -- {quotedPath}").ConfigureAwait(false);
                    string chownUserDirRWX = await Shell.ExecuteAsync($"sudo chmod 2770 -- {quotedPath}").ConfigureAwait(false);

                    if (chownUserDirOwnRoot == "" && chownUserDirRWX == "")
                    {
                        var parentDir = GetDirectoryCore(dirData.ParentDirectory);

                        if (parentDir == null)
                        {
                            Logger.LogToFile("[RoofClientFileSystemManager] - CreateDirectory(): Parent directory returned null.", LogSeverity.Warning);
                            return false;
                        }
                        else
                        {
                            if (dirData.ParentDirectory == _baseDirectory.HashedPath)
                            {
                                if (_baseDirectory.SubDirectories == null)
                                    _baseDirectory.SubDirectories = new HashSet<string>();

                                if (_baseDirectory.SubDirectories.Contains(dirData.HashedPath) == false)
                                    _baseDirectory.SubDirectories.Add(dirData.HashedPath);
                                else
                                    return false;
                                
                                _loadedTopLevelDirectories.Add(dirData.HashedPath, dirData);
                                
                                return true;
                            }

                            if (parentDir.SubDirectories == null)
                                parentDir.SubDirectories = new HashSet<string>();

                            if (parentDir.SubDirectories.Contains(dirData.HashedPath) == false)
                                parentDir.SubDirectories.Add(dirData.HashedPath);
                            else
                                return false;

                            _loadedSubDirectories.Add(dirData.HashedPath, dirData);

                            return true;
                        }
                    }
                    else
                    {
                        Logger.LogToFile($"[RoofClientFileSystemManager] - CreateDirectory(): Failed to set directory ownership and permissions.", LogSeverity.Warning);
                        Logger.LogToFile(chownUserDirOwnRoot, LogSeverity.Warning);
                        Logger.LogToFile(chownUserDirRWX, LogSeverity.Warning);

                        return false;
                    }
                }
                else
                    return false;
            }
            catch(Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - CreateDirectory(): Exception occurred while trying to create a directory.", ex);
                return false;
            }
        }

        private bool UpdateDirectoryCore(string updatingDirHashedPath, RoofDirectoryData updatedDirData)
        {
            try
            {
                if (updatedDirData == null || !SecurityManager.IsValidFilename(updatedDirData.Name))
                    return false;
                SafePath.CanonicalizeUnder(updatedDirData.Path, _basePath);
                var oldDirData = GetDirectoryCore(updatingDirHashedPath);
                if (oldDirData == null || oldDirData.ParentDirectory == null || updatedDirData == null)
                    return false;

                var oldParent = GetDirectoryCore(oldDirData.ParentDirectory);
                var newParent = GetDirectoryCore(updatedDirData.ParentDirectory ?? oldDirData.ParentDirectory);
                if (oldParent == null || newParent == null)
                    return false;

                var oldPath = oldDirData.Path;
                var pathChanged = oldPath != updatedDirData.Path;
                var parentChanged = newParent.HashedPath != oldParent.HashedPath;
                var newPath = System.IO.Path.TrimEndingDirectorySeparator(SafePath.CanonicalizeUnder(pathChanged
                    ? updatedDirData.Path
                    : System.IO.Path.Combine(newParent.Path, updatedDirData.Name), _basePath));
                var newName = System.IO.Path.GetFileName(newPath);
                var parentPath = System.IO.Path.GetDirectoryName(newPath);
                if (parentPath == null || string.IsNullOrEmpty(newName))
                    return false;

                if (pathChanged && !parentChanged)
                {
                    newParent = GetDirectoryCore(Security.XxHash64.GenerateHashedPath(parentPath));
                    if (newParent == null)
                        return false;
                }

                var oldPrefix = oldPath + System.IO.Path.DirectorySeparatorChar;
                if (parentPath != System.IO.Path.GetFullPath(newParent.Path) ||
                    (oldDirData.Name != updatedDirData.Name && updatedDirData.Name != newName) ||
                    newPath.StartsWith(oldPrefix, StringComparison.Ordinal) ||
                    !Directory.Exists(oldPath))
                    return false;

                // Snapshot every cached descendant before changing any paths or dictionary keys.
                var directories = _loadedTopLevelDirectories.Values.Concat(_loadedSubDirectories.Values)
                    .OfType<RoofDirectoryData>()
                    .Where(d => d.Path == oldPath || d.Path.StartsWith(oldPrefix, StringComparison.Ordinal))
                    .Distinct().ToList();
                var files = _loadedTopLevelFiles.Values.Concat(_loadedSubFiles.Values)
                    .OfType<RoofFileData>()
                    .Where(f => f.Path.StartsWith(oldPrefix, StringComparison.Ordinal))
                    .Distinct().ToList();

                string Relocate(string path) => path == oldPath
                    ? newPath : System.IO.Path.Combine(newPath, System.IO.Path.GetRelativePath(oldPath, path));
                var directoryMoves = directories.Select(d => (
                    Data: d, OldHash: d.HashedPath, Path: Relocate(d.Path),
                    Hash: Security.XxHash64.GenerateHashedPath(Relocate(d.Path)),
                    Parent: d == oldDirData ? newParent.HashedPath
                        : Security.XxHash64.GenerateHashedPath(Relocate(System.IO.Path.GetDirectoryName(d.Path)!))
                )).ToList();
                var fileMoves = files.Select(f => (
                    Data: f, OldHash: f.HashedPath, Path: Relocate(f.Path),
                    Hash: Security.XxHash64.GenerateHashedPath(Relocate(f.Path)),
                    Parent: Security.XxHash64.GenerateHashedPath(Relocate(System.IO.Path.GetDirectoryName(f.Path)!))
                )).ToList();

                if (directories.Any(d => !Directory.Exists(d.Path)) || files.Any(f => !File.Exists(f.Path)))
                    return false;

                ValidateTree(oldPath);
                var moved = oldPath != newPath;
                if (moved)
                    Directory.Move(SafePath.CanonicalizeUnder(oldPath, _basePath), SafePath.CanonicalizeUnder(newPath, _basePath));

                // Verify the physical subtree and read its metadata before publishing the new identities.
                Dictionary<string, (int Size, DateTime Modified)> directoryMetadata;
                Dictionary<string, (int Size, DateTime Modified)> fileMetadata;
                try
                {
                    if (!Directory.Exists(newPath) || (moved && Directory.Exists(oldPath)) ||
                        directoryMoves.Any(d => !Directory.Exists(d.Path)) ||
                        fileMoves.Any(f => !File.Exists(f.Path)))
                        throw new IOException("Directory move did not produce the expected physical subtree.");

                    directoryMetadata = directoryMoves.ToDictionary(d => d.Hash,
                        d => (checked((int)GetDirectorySize(new DirectoryInfo(d.Path))), Directory.GetLastWriteTimeUtc(d.Path)));
                    fileMetadata = fileMoves.ToDictionary(f => f.Hash,
                        f => (checked((int)new FileInfo(f.Path).Length), File.GetLastWriteTimeUtc(f.Path)));
                }
                catch
                {
                    if (moved)
                        Directory.Move(SafePath.CanonicalizeUnder(newPath, _basePath), SafePath.CanonicalizeUnder(oldPath, _basePath));
                    throw;
                }

                foreach (var directory in directoryMoves)
                {
                    _loadedTopLevelDirectories.Remove(directory.OldHash);
                    _loadedSubDirectories.Remove(directory.OldHash);
                }
                foreach (var file in fileMoves)
                {
                    _loadedTopLevelFiles.Remove(file.OldHash);
                    _loadedSubFiles.Remove(file.OldHash);
                }
                oldParent.SubDirectories?.Remove(updatingDirHashedPath);

                foreach (var directory in directoryMoves)
                {
                    var data = directory.Data;
                    data.Path = directory.Path;
                    data.Name = System.IO.Path.GetFileName(directory.Path);
                    data.SetHashedPath(directory.Hash);
                    data.ParentDirectory = directory.Parent;
                    data.Size = directoryMetadata[directory.Hash].Size;
                    data.LastModifiedUTC = directoryMetadata[directory.Hash].Modified;
                    data.SubDirectories = directoryMoves.Where(d => d.Parent == directory.Hash).Select(d => d.Hash).ToHashSet();
                    data.Files = fileMoves.Where(f => f.Parent == directory.Hash).Select(f => f.Hash).ToHashSet();
                    var cache = directory.Parent == _baseDirectory?.HashedPath
                        ? _loadedTopLevelDirectories : _loadedSubDirectories;
                    cache[directory.Hash] = data;
                }
                foreach (var file in fileMoves)
                {
                    file.Data.Path = file.Path;
                    file.Data.SetHashedPath(file.Hash);
                    file.Data.ParentDirectory = file.Parent;
                    file.Data.Size = fileMetadata[file.Hash].Size;
                    file.Data.LastModifiedUTC = fileMetadata[file.Hash].Modified;
                    _loadedSubFiles[file.Hash] = file.Data;
                }
                newParent.SubDirectories ??= new HashSet<string>();
                newParent.SubDirectories.Add(oldDirData.HashedPath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - UpdateDirectory(): Exception occurred while trying to update a directory.", ex);
                return false;
            }
        }
        
        private bool CopyDirectoryCore(string copyingDirectoryHashedPath, string targetDirectoryHashedPath)
        {
            string? destinationPath = null;
            var createdDestination = false;
            try
            {
                var source = GetDirectoryCore(copyingDirectoryHashedPath);
                var target = GetDirectoryCore(targetDirectoryHashedPath);
                if (source == null || source.ParentDirectory == null || target == null)
                    return false;

                var sourcePath = SafePath.CanonicalizeUnder(source.Path, _basePath);
                destinationPath = SafePath.CanonicalizeUnder(Path.Combine(target.Path, source.Name), _basePath);
                if (!Directory.Exists(sourcePath) || !Directory.Exists(target.Path) ||
                    destinationPath == sourcePath ||
                    destinationPath.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    Directory.Exists(destinationPath) || File.Exists(destinationPath))
                    return false;

                ValidateTree(sourcePath);

                // Keep the copy private until the entire physical tree and its metadata are ready.
                Directory.CreateDirectory(destinationPath);
                createdDestination = true;
                CopyDirectory_Internal(sourcePath, destinationPath);

                var copiedDirectories = new Dictionary<string, RoofDirectoryData>();
                var copiedFiles = new Dictionary<string, RoofFileData>();
                var copiedRoot = LoadCopiedDirectory(new DirectoryInfo(destinationPath), target.HashedPath,
                    copiedDirectories, copiedFiles);

                // Never overwrite an existing cache entry, including stale entries for a missing path.
                if (copiedDirectories.Keys.Any(hash => _loadedTopLevelDirectories.ContainsKey(hash) || _loadedSubDirectories.ContainsKey(hash)) ||
                    copiedFiles.Keys.Any(hash => _loadedTopLevelFiles.ContainsKey(hash) || _loadedSubFiles.ContainsKey(hash)))
                    throw new IOException("The copied tree conflicts with an existing cache entry.");

                foreach (var directory in copiedDirectories.Values)
                {
                    var cache = directory.ParentDirectory == _baseDirectory?.HashedPath
                        ? _loadedTopLevelDirectories : _loadedSubDirectories;
                    cache.Add(directory.HashedPath, directory);
                }
                foreach (var file in copiedFiles.Values)
                    _loadedSubFiles.Add(file.HashedPath, file);

                target.SubDirectories ??= new HashSet<string>();
                target.SubDirectories.Add(copiedRoot.HashedPath);
                return true;
            }
            catch (Exception ex)
            {
                // Only remove the new tree created by this operation; existing destinations are rejected above.
                if (createdDestination && destinationPath != null)
                {
                    try
                    {
                        ValidateTree(destinationPath);
                        Directory.Delete(destinationPath, true);
                    }
                    catch (Exception cleanupException)
                    {
                        Logger.LogExceptionToFile("[RoofClientFileSystemManager] - CopyDirectory(): Failed to remove incomplete copy.", cleanupException);
                    }
                }
                Logger.LogExceptionToFile("[RoofClientFileSystemManager] - CopyDirectory(): Exception occurred while trying to copy a directory.", ex);
                return false;
            }
        }

        private void CopyDirectory_Internal(string sourceDir, string destinationDir)
        {
            sourceDir = SafePath.CanonicalizeUnder(sourceDir, _basePath);
            destinationDir = SafePath.CanonicalizeUnder(destinationDir, _basePath);
            var source = new DirectoryInfo(sourceDir);
            if (!source.Exists)
                throw new DirectoryNotFoundException(sourceDir);
            // Do not follow directory links into cycles or outside the source tree.
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Copying symbolic links is not supported.");

            Directory.CreateDirectory(destinationDir);
            foreach (var file in source.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Copying symbolic links is not supported.");
                file.CopyTo(SafePath.CanonicalizeUnder(Path.Combine(destinationDir, file.Name), _basePath));
            }
            foreach (var child in source.GetDirectories())
                CopyDirectory_Internal(child.FullName, Path.Combine(destinationDir, child.Name));

            Directory.SetLastWriteTimeUtc(destinationDir, source.LastWriteTimeUtc);
        }

        private RoofDirectoryData LoadCopiedDirectory(DirectoryInfo directory, string parentHash,
            Dictionary<string, RoofDirectoryData> directories, Dictionary<string, RoofFileData> files)
        {
            SafePath.CanonicalizeUnder(directory.FullName, _basePath);
            // Enumerate the destination, not the source cache, so newly discovered children are included.
            var data = new RoofDirectoryData
            {
                Name = directory.Name,
                Path = directory.FullName,
                ParentDirectory = parentHash,
                Files = new HashSet<string>(),
                SubDirectories = new HashSet<string>()
            };
            directories.Add(data.HashedPath, data);
            long size = 0;
            foreach (var file in directory.GetFiles())
            {
                SafePath.CanonicalizeUnder(file.FullName, _basePath);
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                var fileData = new RoofFileData
                {
                    Name = file.Name,
                    Path = file.FullName,
                    ParentDirectory = data.HashedPath,
                    Size = checked((int)stream.Length),
                    FileHash = Security.XxHash64.ComputeHash(stream),
                    LastModifiedUTC = File.GetLastWriteTimeUtc(file.FullName)
                };
                files.Add(fileData.HashedPath, fileData);
                data.Files.Add(fileData.HashedPath);
                size += fileData.Size;
            }
            foreach (var child in directory.GetDirectories())
            {
                var childData = LoadCopiedDirectory(child, data.HashedPath, directories, files);
                data.SubDirectories.Add(childData.HashedPath);
                size += childData.Size;
            }
            data.Size = checked((int)size);
            data.LastModifiedUTC = Directory.GetLastWriteTimeUtc(directory.FullName);
            return data;
        }

        private (bool, string) DeleteDirectoryCore(string hashedPath)
        {
            try
            {
                var getdirResult = GetDirectoryCore(hashedPath);

                if (getdirResult == null)
                {
                    Logger.LogToFile($"[RoofClientFileSystemManager] - DeleteDirectory(): Failed to find local directory that matches hashed path {hashedPath}");
                    return (false, "Failed to find matching directory.");
                }

                //Can't delete the base directory.
                if (getdirResult == _baseDirectory)
                    return (false, "Cannot delete base directory.");

                var parentDir = GetDirectoryCore(getdirResult.ParentDirectory);

                if (parentDir == null)
                    return (false, "Failed to find parent directory.");

                ValidateTree(getdirResult.Path);

                //Unlink
                if (parentDir.SubDirectories != null && parentDir.SubDirectories.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from parent's sub directories.");

                if (_loadedTopLevelDirectories.ContainsKey(hashedPath) && _loadedTopLevelDirectories.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from top level directories.");

                if (_loadedSubDirectories.ContainsKey(hashedPath) && _loadedSubDirectories.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from sub level directories.");

                //Actually delete.
                Directory.Delete(SafePath.CanonicalizeUnder(getdirResult.Path, _basePath), true);

                if (Directory.Exists(getdirResult.Path) == true)
                    return (false, "Failed to physically delete the file.");

                //Delete from the memory.
                getdirResult = null;

                return (true, "");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - DeleteDirectory(): Exception occurred while trying to delete a directory.", ex);
                return (false, ex.Message);
            }
        }

        private RoofFileData? GetFileCore(string hashedPath)
        {
            if (string.IsNullOrEmpty(hashedPath))
                return null;

            if (_loadedTopLevelFiles.ContainsKey(hashedPath))
            {
                var foundFile = _loadedTopLevelFiles[hashedPath];

                if (foundFile == null) return null;
                ValidatePath(foundFile.Path, hashedPath);
                if (!File.Exists(foundFile.Path))
                {
                    Logger.LogToFile("[RoofClientFileSystemManager] - GetFile(): File could not be found in local FS.");
                    //UNLINK
                    return null;
                }
                
                //Update hash and last write time....
                try
                {
                    using (var fs = new FileStream(foundFile.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length > 0)
                        {
                            var runtimeFileHash = Security.XxHash64.ComputeHash(fs);

                            if (string.IsNullOrEmpty(foundFile.FileHash) ||
                                !string.Equals(foundFile.FileHash, runtimeFileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                foundFile.FileHash = runtimeFileHash;
                            }
                        }
                    }

                    foundFile.LastModifiedUTC = File.GetLastWriteTimeUtc(foundFile.Path);
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile("[RoofClientFileSystemManager] - GetFile(): Could not update file hash.", ex);
                    return foundFile;
                }

                return foundFile;
            }

            if (_loadedSubFiles.ContainsKey(hashedPath))
            {
                var foundFile = _loadedSubFiles[hashedPath];

                if (foundFile == null) return null;
                ValidatePath(foundFile.Path, hashedPath);
                if (!File.Exists(foundFile.Path))
                {
                    Logger.LogToFile("[RoofClientFileSystemManager] - GetFile(): File could not be found in local FS.");
                    //UNLINK
                    return null;
                }
                
                //Update hash and last write time....
                try
                {
                    using (var fs = new FileStream(foundFile.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length > 0)
                        {
                            var runtimeFileHash = Security.XxHash64.ComputeHash(fs);

                            if (string.IsNullOrEmpty(foundFile.FileHash) ||
                                !string.Equals(foundFile.FileHash, runtimeFileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                foundFile.FileHash = runtimeFileHash;
                            }
                        }
                    }

                    foundFile.LastModifiedUTC = File.GetLastWriteTimeUtc(foundFile.Path);
                }
                catch (Exception ex)
                {
                    Logger.LogExceptionToFile("[RoofClientFileSystemManager] - GetFile(): Could not update file hash.", ex);
                    return foundFile;
                }

                return foundFile;
            }


            return null; // File not found
        }

        private async Task<bool> CreateFileCore(RoofFileData fileData)
        {
            try
            {
                var safePath = ValidatePath(fileData.Path, fileData.HashedPath);
                var parent = GetDirectoryCore(fileData.ParentDirectory);
                if (parent == null || !SecurityManager.IsValidFilename(fileData.Name) ||
                    Path.GetFileName(safePath) != fileData.Name || Path.GetDirectoryName(safePath) != parent.Path ||
                    Directory.Exists(safePath) || File.Exists(safePath))
                    return false;

                fileData.Path = safePath;
                
                if (System.IO.File.Exists(safePath))
                    return false;

                var quotedPath = SafePath.ShellQuote(safePath);
                
                using(var fileStream = new FileStream(safePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    string chownUserFileOwnRoot = await Shell.ExecuteAsync($"sudo chown root:roofsftpusers -- {quotedPath}").ConfigureAwait(false);
                    string chownUserFileRWX = await Shell.ExecuteAsync($"sudo chmod 770 -- {quotedPath}").ConfigureAwait(false);

                    if (chownUserFileOwnRoot == "" && chownUserFileRWX == "")
                    {
                        var parentDir = GetDirectoryCore(fileData.ParentDirectory);

                        if (parentDir == null)
                        {
                            Logger.LogToFile("[RoofClientFileSystemManager] - CreateFile(): Parent directory returned null.", LogSeverity.Warning);
                            return false;
                        }
                        else
                        {
                            if (fileData.ParentDirectory == _baseDirectory.HashedPath)
                            {
                                if (_baseDirectory.Files == null)
                                    _baseDirectory.Files = new HashSet<string>();

                                if(_baseDirectory.Files.Contains(fileData.HashedPath) == false)
                                    _baseDirectory.Files.Add(fileData.HashedPath);

                                if(_loadedTopLevelFiles.ContainsKey(fileData.HashedPath) == false)
                                    _loadedTopLevelFiles.Add(fileData.HashedPath, fileData);

                                return true;
                            }

                            if (parentDir.Files == null)
                                parentDir.Files = new HashSet<string>();

                            if (parentDir.Files.Contains(fileData.HashedPath) == false)
                                parentDir.Files.Add(fileData.HashedPath);

                            if (_loadedSubFiles.ContainsKey(fileData.HashedPath) == false)
                                _loadedSubFiles.Add(fileData.HashedPath, fileData);

                            return true;
                        }
                    }
                    else
                    {
                        Logger.LogToFile($"[RoofClientFileSystemManager] - CreateFile(): Failed to set file ownership and permissions.", LogSeverity.Warning);
                        Logger.LogToFile(chownUserFileOwnRoot, LogSeverity.Warning);
                        Logger.LogToFile(chownUserFileRWX, LogSeverity.Warning);

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - CreateFile(): Exception occurred while trying to create a file.", ex);
                return false;
            }
        }

        private async Task<(bool, RoofFileData?)> UpdateFileCore(RoofFileData fileData)
        {
            try
            {
                if (fileData == null || !SecurityManager.IsValidFilename(fileData.Name))
                    return (false, null);
                SafePath.CanonicalizeUnder(fileData.Path, _basePath);
                var getFileResult = GetFileCore(fileData.HashedPath);

                if (getFileResult == null)
                {
                    Logger.LogToFile($"[RoofClientFileSystemManager] - UpdateFile(): Failed to find local file that matches hashed path {fileData.HashedPath}");
                    return (false, null);
                }

                // Validate write flags before making any filesystem changes.
                if (fileData.Data != null && fileData.ShouldOverwriteData == fileData.ShouldAppendData)
                {
                    Logger.LogToFile("[RoofClientFileSystemManager] - UpdateFile(): Invalid data write flags.");
                    return (false, null);
                }

                var nameChanged = getFileResult.Name != fileData.Name;
                var pathChanged = getFileResult.Path != fileData.Path;
                var parentChanged = getFileResult.ParentDirectory != fileData.ParentDirectory;

                if (nameChanged || pathChanged || parentChanged)
                {
                    var oldPath = getFileResult.Path;
                    var oldHashedPath = getFileResult.HashedPath;
                    var oldParent = GetDirectoryCore(getFileResult.ParentDirectory);
                    var newParent = GetDirectoryCore(fileData.ParentDirectory);
                    if (oldParent == null || newParent == null)
                        return (false, null);

                    // A name or parent change alone also implies a new physical path.
                    var newPath = SafePath.CanonicalizeUnder(pathChanged
                        ? fileData.Path
                        : System.IO.Path.Combine(newParent.Path, fileData.Name), _basePath);
                    var newName = System.IO.Path.GetFileName(newPath);
                    var parentPath = System.IO.Path.GetDirectoryName(newPath);
                    if (parentPath == null || string.IsNullOrEmpty(newName))
                        return (false, null);

                    if (pathChanged && !parentChanged)
                    {
                        newParent = GetDirectoryCore(Security.XxHash64.GenerateHashedPath(parentPath));
                        if (newParent == null)
                            return (false, null);
                    }

                    // Reject conflicting destination fields instead of caching inconsistent metadata.
                    if (parentPath != System.IO.Path.GetFullPath(newParent.Path) ||
                        (nameChanged && fileData.Name != newName))
                        return (false, null);

                    Logger.LogToFile($"[RoofClientFileSystemManager] - UpdateFile(): Moving file to {newPath}");
                    if (oldPath != newPath)
                        File.Move(SafePath.CanonicalizeUnder(oldPath, _basePath), SafePath.CanonicalizeUnder(newPath, _basePath));

                    // Publish the new identity only after the physical move succeeds.
                    getFileResult.Name = newName;
                    getFileResult.Path = newPath;
                    getFileResult.ParentDirectory = newParent.HashedPath;
                    getFileResult.SetHashedPath(Security.XxHash64.GenerateHashedPath(newPath));

                    _loadedTopLevelFiles.Remove(oldHashedPath);
                    _loadedSubFiles.Remove(oldHashedPath);
                    oldParent.Files?.Remove(oldHashedPath);

                    var destinationCache = newParent.HashedPath == _baseDirectory?.HashedPath
                        ? _loadedTopLevelFiles
                        : _loadedSubFiles;
                    destinationCache[getFileResult.HashedPath] = getFileResult;
                    newParent.Files ??= new HashSet<string>();
                    newParent.Files.Add(getFileResult.HashedPath);
                }

                if (fileData.Data != null)
                {
                    var computedHash = Security.XxHash64.ComputeHash(fileData.Data);
                    // Appending must run even when the appended bytes equal the existing contents.
                    if (fileData.ShouldAppendData || getFileResult.FileHash != computedHash)
                    {
                        var mode = fileData.ShouldAppendData ? FileMode.Append : FileMode.Create;
                        using (var fileStream = new FileStream(SafePath.CanonicalizeUnder(getFileResult.Path, _basePath), mode, FileAccess.Write,
                            FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            await fileStream.WriteAsync(fileData.Data, 0, fileData.Data.Length).ConfigureAwait(false);
                            await fileStream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                }

                // Derive metadata from the final file, including empty files and appended content.
                using (var fileStream = new FileStream(SafePath.CanonicalizeUnder(getFileResult.Path, _basePath), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    getFileResult.FileHash = Security.XxHash64.ComputeHash(fileStream);
                    getFileResult.Size = checked((int)fileStream.Length);
                }
                getFileResult.LastModifiedUTC = File.GetLastWriteTimeUtc(getFileResult.Path);

                return (true, getFileResult);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - UpdateFile(): Exception occurred while trying to update a file.", ex);
                return (false, null);
            }
        }

        private (bool, string) DeleteFileCore(string hashedPath)
        {
            try
            {
                var getFileResult = GetFileCore(hashedPath);

                if (getFileResult == null)
                {
                    Logger.LogToFile($"[RoofClientFileSystemManager] - DeleteFile(): Failed to find local file that matches hashed path {hashedPath}");
                    return (false, "Failed to find matching file.");
                }

                var parentDir = GetDirectoryCore(getFileResult.ParentDirectory);

                if (parentDir == null)
                    return (false, "Failed to find parent directory.");

                //Unlink
                if (parentDir.Files != null && parentDir.Files.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from parent directory files.");

                if (_loadedTopLevelFiles.ContainsKey(hashedPath) && _loadedTopLevelFiles.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from top level files.");

                if (_loadedSubFiles.ContainsKey(hashedPath) && _loadedSubFiles.Remove(hashedPath) == false)
                    return (false, "Failed to unlink from sub level files.");

                //Actually delete.
                System.IO.File.Delete(SafePath.CanonicalizeUnder(getFileResult.Path, _basePath));

                if(System.IO.File.Exists(getFileResult.Path))
                    return (false, "Failed to physically delete the file.");

                //Delete from the memory.
                getFileResult = null;

                return (true, "");
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - DeleteFile(): Exception occurred while trying to delete a file.", ex);
                return (false, ex.Message);
            }
        }
        
        private async Task<(bool, string)> GetFileDataCore(string fileHashedPath)
        {
            try
            {
                var getFileResult = GetFileCore(fileHashedPath);

                if (getFileResult == null)
                {
                    Logger.LogToFile($"[RoofClientFileSystemManager] - GetFileData(): Failed to find local file that matches hashed path {fileHashedPath}");
                    return (false, "Failed to find requested file.");
                }

                var fileDataRaw = await File.ReadAllBytesAsync(SafePath.CanonicalizeUnder(getFileResult.Path, _basePath)).ConfigureAwait(false);
                var fileDataBase64 = Convert.ToBase64String(fileDataRaw);
                
                return (true, fileDataBase64);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[RoofClientFileSystemManager] - GetFileData(): Exception occurred while trying to get file data.", ex);
                return (false, ex.Message);
            }
        }

        private long GetDirectorySize(DirectoryInfo d)
        {
            SafePath.CanonicalizeUnder(d.FullName, _basePath);
            long size = 0;

            // Add file sizes.
            FileInfo[] fis = d.GetFiles();
            foreach (FileInfo fi in fis)
            {
                SafePath.CanonicalizeUnder(fi.FullName, _basePath);
                size += fi.Length;
            }

            // Add subdirectory sizes.
            DirectoryInfo[] dis = d.GetDirectories();
            foreach (DirectoryInfo di in dis)
                size += GetDirectorySize(di);

            return size;
        }
    }
}
