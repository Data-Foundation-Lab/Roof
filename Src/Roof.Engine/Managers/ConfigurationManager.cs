using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roof.Engine.Tools;
using Roof.Common.Tools;

//typedef std::string RoofClientID
using RoofClientID = string;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Roof.Engine.Managers
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RoofConfiguration))]
    public partial class RoofConfigurationJSONContext : JsonSerializerContext
    {
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RoofClientConfig))]
    public partial class RoofClientConfigJSONContext : JsonSerializerContext
    {
    }

    public class RoofClientConfig
    {
        public int AccessCount { get; set; }
        public string OSSystemUsername { get; set; }
    }

    public partial class RoofConfiguration
    {
        ///////////RAW///////////
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sha256FileHash { get; set; }
        ///////////RAW///////////

        ///////////ENCRYPTED///////////
        public bool ManageHost { get; set; }
        public int SchemaVersion { get; set; }
        public string RoofIdentifier { get; set; }
        public bool HasCompletedInitialSetup { get; set; }
        public Dictionary<RoofClientID, RoofClientConfig> RegisteredClients { get; set; }
        ///////////ENCRYPTED///////////
    }

    public class ConfigurationManager
    {
        internal const int CURRENT_CONFIG_FILE_SCHEMA_VERSION = 3;

        private const UnixFileMode PRIVATE_FILE_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const UnixFileMode PRIVATE_DIRECTORY_FILE_MODE = PRIVATE_FILE_FILE_MODE | UnixFileMode.UserExecute;
        private static string KEY_ROOT = Directory.GetCurrentDirectory();
        private static string KeyDirectory => SafePath.CanonicalizeUnder(Path.Combine(KEY_ROOT, "keys"), KEY_ROOT);
        private static string _configurationEncryptionKeyFilePath =>
            SafePath.CanonicalizeUnder(Path.Combine(KeyDirectory, "roof_config.key"), KeyDirectory);
        private static string _configurationFileDirectory = $"{Directory.GetCurrentDirectory()}/roof_config/";
        private static string _configurationFilePath = _configurationFileDirectory + "config.json";

        internal static string ClientKeyPath(string clientID)
        {
            if (!SecurityManager.IsValidClientID(clientID))
                throw new ArgumentException("Invalid client ID.");

            return SafePath.CanonicalizeUnder(Path.Combine(KeyDirectory, $"{clientID}.key"), KeyDirectory);
        }

        private static RoofConfiguration _configuration = new();
        private static bool _installationLoaded;

        private static async Task WriteEncryptionKey(string path, string nek, string niv, bool overwrite)
        {
            byte[] keyBytes = Convert.FromBase64String(nek);
            byte[] ivBytes = Convert.FromBase64String(niv);
            if (keyBytes.Length != 32 || ivBytes.Length != 16)
                throw new ArgumentException("Encryption keys require a 32-byte key and a 16-byte IV.");

            var safeKeyDir = KeyDirectory;
            var safeKeyFile = SafePath.CanonicalizeUnder(path, safeKeyDir);

            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(safeKeyDir);
            else
                Directory.CreateDirectory(safeKeyDir, PRIVATE_DIRECTORY_FILE_MODE);

            if (_configuration.ManageHost)
            {
                string result = await Shell.ExecuteAsync(
                    $"sudo chown root:root -- {SafePath.ShellQuote(safeKeyDir)} && sudo chmod 700 -- {SafePath.ShellQuote(safeKeyDir)}");
                if (!string.IsNullOrEmpty(result))
                    throw new IOException("Failed to secure encryption key directory: " + result);
            }
            else
            {
                if (OperatingSystem.IsWindows())
                {
                    var fInfo = new FileInfo(safeKeyDir);
                    var fSec = fInfo.GetAccessControl();
                    var fRule = new FileSystemAccessRule(
                        new NTAccount("BUILTIN\\Users"),
                        FileSystemRights.ReadAndExecute,
                        AccessControlType.Allow);

                    fSec.AddAccessRule(fRule);
                    fInfo.SetAccessControl(fSec);
                }
                else
                    File.SetUnixFileMode(safeKeyDir, PRIVATE_DIRECTORY_FILE_MODE);
            }

            // Write privately, then replace atomically so failed rotation leaves the old key intact.
            string temporaryPath = safeKeyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    //UnixCreateMode = PRIVATE_FILE_FILE_MODE
                }))
                {
                    await stream.WriteAsync(keyBytes.Concat(ivBytes).ToArray());
                }

                if (_configuration.ManageHost)
                {
                    string result = await Shell.ExecuteAsync(
                        $"sudo chown root:root -- {SafePath.ShellQuote(temporaryPath)} && sudo chmod 600 -- {SafePath.ShellQuote(temporaryPath)}");
                    if (!string.IsNullOrEmpty(result))
                        throw new IOException("Failed to secure encryption key file: " + result);
                }

                SafePath.CanonicalizeUnder(safeKeyFile, safeKeyDir);
                File.Move(temporaryPath, safeKeyFile, overwrite);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static async Task<bool> CreateConfigEncryptionKey()
        {
            try
            {
                var encryptionKey = Security.AES256.GenerateAESIVAndKey();
                await WriteEncryptionKey(_configurationEncryptionKeyFilePath, encryptionKey.Item2, encryptionKey.Item1, false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ConfigurationManager]: Failed to create configuration encryption key.", ex);
                return false;
            }
        }

        private static async Task<bool> CreateRoofClientNetworkEncryptionKey(string clientID, string nek, string niv)
        {
            var path = ClientKeyPath(clientID);
            if (File.Exists(path))
            {
                // A retry must not silently accept an unrelated or damaged existing key.
                var expected = Convert.FromBase64String(nek).Concat(Convert.FromBase64String(niv)).ToArray();
                var existing = await File.ReadAllBytesAsync(path);
                if (Convert.FromBase64String(nek).Length != 32 || Convert.FromBase64String(niv).Length != 16 ||
                    !existing.SequenceEqual(expected))
                    return false;
            }

            return await SaveRoofClientNetworkEncryptionKey(clientID, nek, niv);
        }

        public static async Task<bool> SaveRoofClientNetworkEncryptionKey(string clientID, string nek, string niv)
        {
            try
            {
                await WriteEncryptionKey(ClientKeyPath(clientID), nek, niv, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ConfigurationManager]: Failed to save roof client network encryption key.", ex);
                return false;
            }
        }

        public static async Task<bool> Initialize()
        {
            try
            {
                Directory.CreateDirectory(_configurationFileDirectory);
                bool existing = File.Exists(_configurationFilePath);
                KEY_ROOT = Directory.GetCurrentDirectory();
                // For an existing installation locate its key before decrypting ManageHost.

                if (existing)
                {
                    if (!File.Exists(_configurationEncryptionKeyFilePath))
                        KEY_ROOT = "/etc/Roof";
                    var contents = await File.ReadAllBytesAsync(_configurationFilePath);
                    return await LoadConfiguration(contents) != null;
                }

                if (!File.Exists(_configurationEncryptionKeyFilePath) && !await CreateConfigEncryptionKey())
                    return false;

                return await CreateDefaultConfigurationFile();
            }
            catch(Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager]: Exception occured while trying to initialize. Exception: {ex.Message}", LogSeverity.Danger);
                return false;
            }
        }

        internal static Task<bool> SaveLoadedConfiguration()
        {
            return _installationLoaded ? SaveConfiguration(_configuration) : Task.FromResult(true);
        }

        public static RoofConfiguration GetConfiguration() => _configuration;

        public static async Task<bool> SaveConfiguration(RoofConfiguration? configuration)
        {
            try
            {
                if (configuration == null)
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Manager is not initialized properly.", LogSeverity.Danger);
                    return false;
                }

                //Compute hash
                configuration.Sha256FileHash = ComputeConfigurationIntegrityHash(configuration);

                //Serialise
                string serialisedJsonConfig = JsonSerializer.Serialize(
                    configuration,
                    RoofConfigurationJSONContext.Default.RoofConfiguration
                );

                if (!File.Exists(_configurationEncryptionKeyFilePath))
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Failed to find config encryption key.", LogSeverity.Danger);
                    return false;
                }
                
                var readConfigEncryptionKeyContent = await File.ReadAllBytesAsync(_configurationEncryptionKeyFilePath);
                
                if (readConfigEncryptionKeyContent.Length != 48)
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Config encryption key file is invalid.", LogSeverity.Danger);
                    return false;
                }
                
                byte[] readCfgEncKey = readConfigEncryptionKeyContent.Take(32).ToArray();  // AES-256 = 32 bytes
                byte[] readCfgEncIV = readConfigEncryptionKeyContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                string loadedCfgEncKey = Convert.ToBase64String(readCfgEncKey);
                string loadedCfgEncIV = Convert.ToBase64String(readCfgEncIV);
                
                byte[] encryptedConfig = Security.AES256.ManagedNetworkEncrypt(loadedCfgEncKey, loadedCfgEncIV, serialisedJsonConfig);
                
                string temporaryPath = _configurationFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, encryptedConfig);
                    File.Move(temporaryPath, _configurationFilePath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                _configuration = configuration;
                _installationLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager] - SaveConfiguration(): Exception occurred while trying to save configuration. Exception: {ex.Message}", LogSeverity.Danger);
                return false;
            }
        }

        private static async Task<RoofConfiguration?> LoadConfiguration(byte[] readEncryptedConfigFileContent)
        {
            try
            {
                if (!File.Exists(_configurationEncryptionKeyFilePath))
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Failed to find config encryption key.", LogSeverity.Danger);
                    return null;
                }
                
                var readConfigEncryptionKeyContent = await File.ReadAllBytesAsync(_configurationEncryptionKeyFilePath);
                
                if (readConfigEncryptionKeyContent.Length != 48)
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Config encryption key file is invalid.", LogSeverity.Danger);
                    return null;
                }
                
                byte[] readCfgEncKey = readConfigEncryptionKeyContent.Take(32).ToArray();  // AES-256 = 32 bytes
                byte[] readCfgEncIV = readConfigEncryptionKeyContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                string loadedCfgEncKey = Convert.ToBase64String(readCfgEncKey);
                string loadedCfgEncIV = Convert.ToBase64String(readCfgEncIV);
                
                string configFileContent = Security.AES256.ManagedNetworkDecrypt(loadedCfgEncKey, loadedCfgEncIV, readEncryptedConfigFileContent, readEncryptedConfigFileContent.Length);

                if (!configFileContent.IsStringJson())
                {
                    Logger.LogToFile("[ConfigurationManager] -  LoadConfiguration(): Failed to decrypt config file.", LogSeverity.Danger);
                    return null;
                }
                
                RoofConfiguration? deserialisedConfig = JsonSerializer.Deserialize<RoofConfiguration>(configFileContent, RoofConfigurationJSONContext.Default.RoofConfiguration);

                if(deserialisedConfig == null)
                {
                    Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): deserialisedConfig returned null.");
                    return null;
                }
                
                //Compute runtime hash and check...
                string? originalHash = deserialisedConfig.Sha256FileHash;
                if (string.IsNullOrWhiteSpace(originalHash))
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Missing stored config integrity hash.", LogSeverity.Danger);
                    return null;
                }
                
                string computedRuntimeHash = deserialisedConfig.SchemaVersion < CURRENT_CONFIG_FILE_SCHEMA_VERSION
                    ? ComputeLegacyConfigurationIntegrityHash(configFileContent)
                    : ComputeConfigurationIntegrityHash(deserialisedConfig);
                if (!computedRuntimeHash.Equals(originalHash))
                {
                    Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): A mismatch has been detected between the runtime hash and the stored hash. Startup blocked. Manual recovery required. No replacement config was generated.", LogSeverity.Danger);
                    return null;
                }
                
                if (deserialisedConfig.ManageHost && KEY_ROOT != "/etc/Roof")
                    throw new InvalidDataException("Managed installations require the appliance key store.");

                if (deserialisedConfig.SchemaVersion < CURRENT_CONFIG_FILE_SCHEMA_VERSION)
                {
                    if (deserialisedConfig.SchemaVersion < 2)
                        deserialisedConfig = await Migrate(configFileContent, deserialisedConfig);
                    deserialisedConfig.SchemaVersion = CURRENT_CONFIG_FILE_SCHEMA_VERSION;

                    // After migration, persist upgraded file
                    if (!await SaveConfiguration(deserialisedConfig))
                        return null;
                }
                
                _configuration = deserialisedConfig;
                _installationLoaded = true;
                return _configuration;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): Exception occurred while trying to load configuration. Exception: {ex.Message}", LogSeverity.Danger);
                return null;
            }
        }

        private static async Task<bool> CreateDefaultConfigurationFile()
        {
            try
            {
                var defaultConfig = new RoofConfiguration()
                {
                    ///////////ENCRYPTED///////////
                    ManageHost = false,
                    SchemaVersion = CURRENT_CONFIG_FILE_SCHEMA_VERSION,
                    RoofIdentifier = Guid.NewGuid().ToString().Replace("-", ""),
                    HasCompletedInitialSetup = false,
                    RegisteredClients = new Dictionary<RoofClientID, RoofClientConfig>()
                    ///////////ENCRYPTED///////////
                };

                return await SaveConfiguration(defaultConfig);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ConfigurationManager] - CreateDefaultConfigurationFile(): Exception occurred while trying to create default configuration.", ex);
                return false;
            }
        }
        
        private static async Task<RoofConfiguration> Migrate(string rawJson, RoofConfiguration current)
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            
            //ROOT LEVEL MIGRATIONS
            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migrating root-level configurations...");
            //ROOT LEVEL MIGRATIONS END

            //CHILDREN LEVEL MIGRATIONS
            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migrating children-level configurations...");
            if (root.TryGetProperty("RegisteredClients", out var clientsEl) &&
                clientsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var clientProp in clientsEl.EnumerateObject())
                {
                    var clientId = clientProp.Name;
                    var clientObj = clientProp.Value;

                    if (current.RegisteredClients == null)
                        current.RegisteredClients = new Dictionary<RoofClientID, RoofClientConfig>();

                    if (!current.RegisteredClients.TryGetValue(clientId, out var clientCfg) || clientCfg == null)
                    {
                        clientCfg = new RoofClientConfig();
                        current.RegisteredClients[clientId] = clientCfg;
                    }

                    // OLD -> NEW mappings
                    if (clientObj.TryGetProperty("NetworkEncryptionKey", out var oldNEK) &&
                        oldNEK.ValueKind == JsonValueKind.String &&
                        clientObj.TryGetProperty("NetworkEncryptionIV", out var oldNIV) &&
                        oldNIV.ValueKind == JsonValueKind.String)
                    {
                        bool createRfCliNEKStatus = await CreateRoofClientNetworkEncryptionKey(clientId, oldNEK.GetString()!, oldNIV.GetString()!);
                        
                        if(!createRfCliNEKStatus)
                            throw new IOException($"Failed to migrate roof client network encryption key for {clientId}.");
                    }
                    else
                    {
                        if (clientObj.TryGetProperty("NetworkEncryptionKey", out _) ||
                            clientObj.TryGetProperty("NetworkEncryptionIV", out _))
                            throw new IOException($"Invalid legacy encryption key for {clientId}.");

                        var keyContent = await File.ReadAllBytesAsync(ClientKeyPath(clientId));
                        if (keyContent.Length != 48 || !await SaveRoofClientNetworkEncryptionKey(clientId,
                                Convert.ToBase64String(keyContent.Take(32).ToArray()),
                                Convert.ToBase64String(keyContent.Skip(32).ToArray())))
                            throw new IOException($"Invalid encryption key for {clientId}.");
                    }
                }
            }

            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migration complete.");
            return current;
        }
        
        private static string ComputeLegacyConfigurationIntegrityHash(string rawJson)
        {
            // Older schemas included client keys in their serialized integrity hash.
            var config = JsonNode.Parse(rawJson)!.AsObject();
            config.Remove("Sha256FileHash");
            return Security.SHA256.ComputeSha256Hash(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string ComputeConfigurationIntegrityHash(RoofConfiguration config)
        {
            //Store the hash value field context
            string? existingHash = config.Sha256FileHash;

            //Exclude the hash value field from serialisation and hash check
            config.Sha256FileHash = null;

            //Generate at-runtime serialised config 
            string canonicalJson = JsonSerializer.Serialize(
                config,
                RoofConfigurationJSONContext.Default.RoofConfiguration
            );

            //Restore original hash value context
            config.Sha256FileHash = existingHash;

            //Compute runtime hash
            return Security.SHA256.ComputeSha256Hash(canonicalJson);
        }
    }
}